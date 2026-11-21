using System;
using System.Diagnostics;
using System.Threading;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public enum AutoClickState
    {
        Stopped = 0,
        CountingDown = 1,
        Running = 2,
        Paused = 3,
        /// <summary>Ended by itself after reaching the configured click count or time limit.</summary>
        Finished = 4
    }

    /// <summary>
    /// Classic autoclicker. Lifecycle (start/pause/resume/stop, single instance, session safety)
    /// is handled by <see cref="RunController"/>; this class only implements the clicking loop.
    /// </summary>
    public class AutoClickService
    {
        private readonly MouseInputService _mouseInput;
        private readonly SafeZoneService _safeZoneService;
        private readonly WindowRestrictionService _windowRestrictionService;
        private readonly StatisticsService _statisticsService;
        private readonly Random _rng = new Random();
        private readonly RunController _runner = new RunController("Autoclick");
        private readonly object _lock = new object();

        // Sub-states inside an active run (only meaningful while the runner is Running)
        private volatile bool _countingDown;
        private volatile bool _waitingForWindow;
        private bool _overOwnWindow;
        private AutoClickStopReason _lastStopReason = AutoClickStopReason.UserButton;

        public AutoClickState State { get; private set; } = AutoClickState.Stopped;
        public bool IsActive => _runner.IsActive;
        /// <summary>Paused by the user (as opposed to waiting for the restricted window).</summary>
        public bool IsUserPaused => _runner.State == RunState.Paused;
        public ClickSettings CurrentSettings { get; set; } = new ClickSettings();

        public long SessionClicksCount { get; private set; }
        public double CurrentCps { get; private set; }
        public TimeSpan ElapsedTime { get; private set; } = TimeSpan.Zero;

        // Events
        public event Action<AutoClickState>? StateChanged;
        public event Action<long, double, TimeSpan>? MetricsUpdated;
        public event Action<int>? CountdownProgress; // 3, 2, 1
        public event Action<string>? StatusMessageUpdated;
        public event Action<int, int>? SafeZoneBlocked;

        public AutoClickService(
            MouseInputService mouseInput,
            SafeZoneService safeZoneService,
            WindowRestrictionService windowRestrictionService,
            StatisticsService statisticsService)
        {
            _mouseInput = mouseInput;
            _safeZoneService = safeZoneService;
            _windowRestrictionService = windowRestrictionService;
            _statisticsService = statisticsService;

            _runner.StateChanged += _ => PublishState();
        }

        #region Run control

        public void Start(ClickSettings settings)
        {
            lock (_lock)
            {
                if (_runner.IsActive) return;

                CurrentSettings = settings.Clone();
                SessionClicksCount = 0;
                CurrentCps = 0;
                ElapsedTime = TimeSpan.Zero;
                _overOwnWindow = false;
                _waitingForWindow = false;
                _countingDown = CurrentSettings.StartDelaySeconds > 0;
                _lastStopReason = AutoClickStopReason.UserButton;

                LoggerService.Log($"[AUTOCLICK START] Mode={CurrentSettings.PositionMode}, Button={CurrentSettings.MouseButton}, CPS={CurrentSettings.ComputedCps:0.0}, RepeatMode={CurrentSettings.RepeatMode}");

                var snapshot = CurrentSettings;
                _runner.TryStart(ctx => RunLoop(ctx, snapshot));
            }
        }

        public void Stop(AutoClickStopReason reason = AutoClickStopReason.UserButton)
        {
            if (!_runner.IsActive) return;

            LoggerService.Log($"[AUTOCLICK STOP] Reason={reason}, Elapsed={ElapsedTime.TotalSeconds:0.0}s, Clicks={SessionClicksCount}");
            _lastStopReason = reason;
            _runner.Stop();

            StatusMessageUpdated?.Invoke(reason == AutoClickStopReason.PanicF12 ? "Parado pelo botão de emergência" : "Parado");
            if (reason == AutoClickStopReason.PanicF12) SoundService.PlayPanicSound();
            else SoundService.PlayStopSound();
        }

        public void Pause()
        {
            if (_runner.Pause())
            {
                StatusMessageUpdated?.Invoke("Pausado. Clique em Continuar para retomar.");
                SoundService.PlayStopSound();
            }
        }

        public void Resume()
        {
            if (_runner.Resume())
            {
                StatusMessageUpdated?.Invoke("Clicando");
                SoundService.PlayStartSound();
            }
        }

        public void TogglePause()
        {
            if (_runner.State == RunState.Paused) Resume();
            else Pause();
        }

        public void Panic() => Stop(AutoClickStopReason.PanicF12);

        private void PublishState()
        {
            AutoClickState next = _runner.State switch
            {
                RunState.Running when _countingDown => AutoClickState.CountingDown,
                RunState.Running when _waitingForWindow => AutoClickState.Paused,
                RunState.Running => AutoClickState.Running,
                RunState.Paused => AutoClickState.Paused,
                RunState.Finished => AutoClickState.Finished,
                _ => AutoClickState.Stopped
            };

            lock (_lock)
            {
                if (next == State) return;
                State = next;
            }
            StateChanged?.Invoke(next);
        }

        #endregion

        #region Clicking loop (worker thread)

        private void RunLoop(RunContext ctx, ClickSettings settings)
        {
            var swOverall = new Stopwatch();
            var swCpsWindow = Stopwatch.StartNew();
            var swRunningLog = Stopwatch.StartNew();
            long clicksInCpsWindow = 0;
            long totalClicks = 0;

            try
            {
                // 1. Optional countdown before the first click
                if (settings.StartDelaySeconds > 0)
                {
                    PublishState();
                    for (int s = settings.StartDelaySeconds; s > 0; s--)
                    {
                        CountdownProgress?.Invoke(s);
                        StatusMessageUpdated?.Invoke($"Iniciando em {s}...");
                        SoundService.PlayCountdownTick();
                        if (!ctx.Sleep(1000)) return;
                    }
                    _countingDown = false;
                }

                PublishState();
                StatusMessageUpdated?.Invoke("Clicando");
                SoundService.PlayStartSound();

                int capturedX = 0, capturedY = 0;
                if (settings.PositionMode == ClickPositionMode.CurrentPosition)
                {
                    var pos = _mouseInput.GetCursorPosition();
                    capturedX = pos.X;
                    capturedY = pos.Y;
                }

                swOverall.Start();

                while (ctx.WaitIfPaused())
                {
                    // Natural end conditions -> Finished (not an error, not a user stop)
                    if (settings.RepeatMode == RepeatMode.Count && settings.RepeatCount > 0 && totalClicks >= settings.RepeatCount)
                    {
                        StatusMessageUpdated?.Invoke($"Concluído: {totalClicks} cliques realizados");
                        SoundService.PlayStopSound();
                        return;
                    }
                    if (settings.RepeatMode == RepeatMode.Duration && settings.RepeatDurationSeconds > 0 &&
                        swOverall.Elapsed.TotalSeconds >= settings.RepeatDurationSeconds)
                    {
                        StatusMessageUpdated?.Invoke($"Concluído: tempo de {settings.RepeatDurationSeconds} s atingido");
                        SoundService.PlayStopSound();
                        return;
                    }

                    // Optional "only while window X is focused": wait, never stop
                    if (!_windowRestrictionService.IsTargetWindowActive())
                    {
                        if (!_waitingForWindow)
                        {
                            _waitingForWindow = true;
                            PublishState();
                            StatusMessageUpdated?.Invoke("Aguardando a janela alvo voltar ao foco");
                        }
                        if (!ctx.Sleep(150)) return;
                        continue;
                    }
                    if (_waitingForWindow)
                    {
                        _waitingForWindow = false;
                        PublishState();
                        StatusMessageUpdated?.Invoke("Clicando");
                    }

                    if (swRunningLog.ElapsedMilliseconds >= 10000)
                    {
                        LoggerService.Log($"[AUTOCLICK RUNNING] Elapsed={swOverall.Elapsed.TotalSeconds:0}s, Clicks={totalClicks}, CPS={CurrentCps:0.0}");
                        swRunningLog.Restart();
                    }

                    if (settings.BurstModeEnabled)
                    {
                        for (int i = 0; i < settings.BurstClicksCount && !ctx.IsCancelled; i++)
                        {
                            if (ExecuteSingleClick(settings, ref capturedX, ref capturedY, totalClicks + 1))
                            {
                                totalClicks++;
                                clicksInCpsWindow++;
                                SessionClicksCount = totalClicks;
                                _statisticsService.RegisterClick();
                            }
                            if (i < settings.BurstClicksCount - 1 && !ctx.PreciseSleep(settings.BurstIntervalMs)) return;
                        }
                        if (!ctx.PreciseSleep(settings.BurstCooldownMs)) return;
                    }
                    else
                    {
                        if (ExecuteSingleClick(settings, ref capturedX, ref capturedY, totalClicks + 1))
                        {
                            totalClicks++;
                            clicksInCpsWindow++;
                            SessionClicksCount = totalClicks;
                            _statisticsService.RegisterClick();
                        }

                        int delayMs = settings.TotalIntervalMs;
                        if (settings.RandomIntervalEnabled)
                        {
                            int min = Math.Min(settings.MinRandomIntervalMs, settings.MaxRandomIntervalMs);
                            int max = Math.Max(settings.MinRandomIntervalMs, settings.MaxRandomIntervalMs);
                            delayMs = _rng.Next(min, max + 1);
                        }
                        if (!ctx.PreciseSleep(Math.Max(1, delayMs))) return;
                    }

                    if (swCpsWindow.ElapsedMilliseconds >= 200)
                    {
                        double windowSeconds = swCpsWindow.Elapsed.TotalSeconds;
                        CurrentCps = windowSeconds > 0 ? Math.Round(clicksInCpsWindow / windowSeconds, 1) : 0;
                        ElapsedTime = swOverall.Elapsed;
                        _statisticsService.UpdateRuntimeStats(CurrentCps, windowSeconds);
                        MetricsUpdated?.Invoke(SessionClicksCount, CurrentCps, ElapsedTime);
                        clicksInCpsWindow = 0;
                        swCpsWindow.Restart();
                    }
                }
            }
            finally
            {
                // Mouse button must never stay held after "press and hold" mode
                if (settings.ClickAction == ClickActionType.HoldDown)
                {
                    _mouseInput.SendMouseUp(settings.MouseButton);
                }

                _countingDown = false;
                _waitingForWindow = false;
                ElapsedTime = swOverall.Elapsed;
                CurrentCps = 0;
                MetricsUpdated?.Invoke(SessionClicksCount, 0, ElapsedTime);
                _statisticsService.SaveStatistics();
                LoggerService.Log($"[AUTOCLICK FINISHED] Clicks={SessionClicksCount}, Duration={ElapsedTime.TotalSeconds:0.0}s, StopReason={_lastStopReason}, Cancelled={ctx.IsCancelled}");
            }
        }

        private bool ExecuteSingleClick(ClickSettings settings, ref int capturedX, ref int capturedY, long clickSeq)
        {
            int targetX = 0, targetY = 0;

            switch (settings.PositionMode)
            {
                case ClickPositionMode.FollowCursor:
                    var curPos = _mouseInput.GetCursorPosition();
                    targetX = curPos.X;
                    targetY = curPos.Y;
                    break;

                case ClickPositionMode.CurrentPosition:
                    targetX = capturedX;
                    targetY = capturedY;
                    break;

                case ClickPositionMode.FixedPosition:
                    targetX = settings.FixedX;
                    targetY = settings.FixedY;
                    break;
            }

            if (settings.RandomPositionEnabled && settings.RandomRadiusPixels > 0)
            {
                var jittered = _mouseInput.ApplyRandomRadius(targetX, targetY, settings.RandomRadiusPixels);
                targetX = jittered.X;
                targetY = jittered.Y;
            }

            // Never click on Gabriel Autoclick itself: only outside the app. Checked before the
            // cursor is moved, so the app never even pulls the cursor onto its own window.
            if (SafeZoneService.IsOverOwnWindow(targetX, targetY))
            {
                if (!_overOwnWindow)
                {
                    _overOwnWindow = true;
                    StatusMessageUpdated?.Invoke("Cursor sobre o Gabriel Autoclick: cliques suspensos até sair da janela");
                }
                return false;
            }
            if (_overOwnWindow)
            {
                _overOwnWindow = false;
                StatusMessageUpdated?.Invoke("Clicando");
            }

            if (_safeZoneService.IsPointInSafeZone(targetX, targetY, out var hitZone))
            {
                SafeZoneBlocked?.Invoke(targetX, targetY);
                StatusMessageUpdated?.Invoke($"Clique bloqueado pela zona protegida \"{hitZone?.Name}\"");
                return false;
            }

            if (settings.PositionMode != ClickPositionMode.FollowCursor)
            {
                _mouseInput.SetCursorPosition(targetX, targetY);
            }

            bool success = _mouseInput.ExecuteClickAction(settings.MouseButton, settings.ClickAction);
            LoggerService.LogClick(clickSeq, targetX, targetY, success ? 1u : 0u, success ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return success;
        }

        #endregion
    }
}
