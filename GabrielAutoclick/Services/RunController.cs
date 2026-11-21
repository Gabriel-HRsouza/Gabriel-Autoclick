using System;
using System.Diagnostics;
using System.Threading;

namespace GabrielAutoclick.Services
{
    /// <summary>Lifecycle of one automation (autoclick, multi-point, macro, background click).</summary>
    public enum RunState
    {
        /// <summary>Not running. Either never started or stopped by the user.</summary>
        Stopped = 0,
        Running = 1,
        /// <summary>Paused by the user; the worker thread is alive and resumes exactly where it was.</summary>
        Paused = 2,
        /// <summary>Ended on its own after completing its configured work (cycles, click count, time limit).</summary>
        Finished = 3
    }

    /// <summary>
    /// Single, shared execution engine for every automation in the app.
    ///
    /// Guarantees:
    ///  - Only one run at a time per controller (TryStart refuses while active).
    ///  - State becomes Running synchronously inside TryStart, so a fast second Start/Stop
    ///    never sees a stale "Stopped" and never spawns a duplicate run.
    ///  - Every run has a session number: when an old worker thread finishes late it can't
    ///    overwrite the state of a newer run (the cause of runs "stopping by themselves").
    ///  - Pause blocks the worker on a gate instead of cancelling it.
    ///  - Work runs on a dedicated background thread, independent of window visibility, focus,
    ///    minimizing or UI timers.
    /// </summary>
    public sealed class RunController
    {
        private readonly object _sync = new object();
        private readonly ManualResetEventSlim _resumeGate = new ManualResetEventSlim(true);
        private readonly string _name;
        private CancellationTokenSource? _cts;
        private int _session;

        public RunController(string name)
        {
            _name = name;
        }

        public RunState State { get; private set; } = RunState.Stopped;
        public bool IsActive => State is RunState.Running or RunState.Paused;
        public string? LastError { get; private set; }

        /// <summary>Raised on every transition (from the caller's thread or the worker thread).</summary>
        public event Action<RunState>? StateChanged;

        /// <summary>Raised after the run stopped because of a real failure reported with <see cref="RunContext.Fail"/>.</summary>
        public event Action<string>? Failed;

        public bool TryStart(Action<RunContext> body)
        {
            CancellationTokenSource cts;
            int session;
            lock (_sync)
            {
                if (IsActive) return false;
                cts = new CancellationTokenSource();
                _cts = cts;
                session = ++_session;
                LastError = null;
                _resumeGate.Set();
                State = RunState.Running;
            }

            LoggerService.Log($"[RUN:{_name}] Sessão {session} iniciada");
            StateChanged?.Invoke(RunState.Running);

            var ctx = new RunContext(cts, _resumeGate);
            var thread = new Thread(() => Execute(body, ctx, cts, session))
            {
                IsBackground = true,
                Name = $"Run-{_name}-{session}",
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
            return true;
        }

        private void Execute(Action<RunContext> body, RunContext ctx, CancellationTokenSource cts, int session)
        {
            RunState final = RunState.Finished;
            try
            {
                body(ctx);
                if (ctx.IsCancelled) final = RunState.Stopped;
            }
            catch (OperationCanceledException)
            {
                final = RunState.Stopped;
            }
            catch (Exception ex)
            {
                LoggerService.LogException($"[RUN:{_name}] Erro inesperado na sessão {session}", ex);
                ctx.Fail("Erro inesperado: " + ex.Message);
                final = RunState.Stopped;
            }

            if (ctx.FailureMessage != null) final = RunState.Stopped;

            bool raise = false;
            lock (_sync)
            {
                // Only the current session may change the state; Stop() may already have done it
                if (session == _session)
                {
                    if (State != RunState.Stopped)
                    {
                        State = final;
                        raise = true;
                    }
                    LastError = ctx.FailureMessage;
                    _cts = null;
                }
            }

            cts.Dispose();
            LoggerService.Log($"[RUN:{_name}] Sessão {session} terminou: {final}{(ctx.FailureMessage != null ? " - " + ctx.FailureMessage : "")}");

            if (raise) StateChanged?.Invoke(final);
            if (ctx.FailureMessage != null && session == _session) Failed?.Invoke(ctx.FailureMessage);
        }

        /// <summary>Stops the current run. Safe to call at any time and from any thread.</summary>
        public void Stop()
        {
            lock (_sync)
            {
                if (!IsActive) return;
                _cts?.Cancel();
                _resumeGate.Set(); // wake a paused worker so it can exit
                State = RunState.Stopped;
            }
            StateChanged?.Invoke(RunState.Stopped);
        }

        public bool Pause()
        {
            lock (_sync)
            {
                if (State != RunState.Running) return false;
                _resumeGate.Reset();
                State = RunState.Paused;
            }
            StateChanged?.Invoke(RunState.Paused);
            return true;
        }

        public bool Resume()
        {
            lock (_sync)
            {
                if (State != RunState.Paused) return false;
                _resumeGate.Set();
                State = RunState.Running;
            }
            StateChanged?.Invoke(RunState.Running);
            return true;
        }

        public void TogglePause()
        {
            if (State == RunState.Paused) Resume();
            else Pause();
        }
    }

    /// <summary>Handed to the worker body: cancellation, pause-aware waiting and failure reporting.</summary>
    public sealed class RunContext
    {
        private readonly CancellationTokenSource _cts;
        private readonly ManualResetEventSlim _resumeGate;

        internal RunContext(CancellationTokenSource cts, ManualResetEventSlim resumeGate)
        {
            _cts = cts;
            _resumeGate = resumeGate;
            Token = cts.Token;
        }

        public CancellationToken Token { get; }
        public bool IsCancelled => Token.IsCancellationRequested;
        public bool IsPaused => !_resumeGate.IsSet;
        public string? FailureMessage { get; private set; }

        /// <summary>Blocks while paused. Returns false if the run was cancelled.</summary>
        public bool WaitIfPaused()
        {
            if (_resumeGate.IsSet) return !IsCancelled;
            try
            {
                _resumeGate.Wait(Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            return !IsCancelled;
        }

        /// <summary>Pause-aware sleep: time spent paused does not count. Returns false if cancelled.</summary>
        public bool Sleep(int milliseconds)
        {
            long remaining = Math.Max(0, milliseconds);
            var sw = new Stopwatch();
            while (true)
            {
                if (!WaitIfPaused()) return false;
                if (remaining <= 0) return true;

                int chunk = (int)Math.Min(remaining, 50);
                sw.Restart();
                if (Token.WaitHandle.WaitOne(chunk)) return false;
                remaining -= Math.Max(1, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>High-precision wait for fast click intervals (sleep + spin). Returns false if cancelled.</summary>
        public bool PreciseSleep(int milliseconds)
        {
            if (!WaitIfPaused()) return false;
            if (milliseconds > 200) return Sleep(milliseconds);
            MouseInputService.PreciseWaitMilliseconds(milliseconds, Token);
            return WaitIfPaused();
        }

        /// <summary>Ends the run because of a real, unrecoverable failure (shown to the user).</summary>
        public void Fail(string message)
        {
            FailureMessage ??= message;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
