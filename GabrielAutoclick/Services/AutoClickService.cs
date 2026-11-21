using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public enum AutoClickState
    {
        Stopped = 0,
        CountingDown = 1,
        Running = 2,
        Paused = 3
    }

    public class AutoClickService
    {
        private readonly MouseInputService _mouseInput;
        private readonly SafeZoneService _safeZoneService;
        private readonly WindowRestrictionService _windowRestrictionService;
        private readonly StatisticsService _statisticsService;
        private readonly Random _rng = new Random();

        private CancellationTokenSource? _cts;
        private Task? _executionTask;
        private readonly object _lock = new object();

        public AutoClickState State { get; private set; } = AutoClickState.Stopped;
        public ClickSettings CurrentSettings { get; set; } = new ClickSettings();

        public long SessionClicksCount { get; private set; } = 0;
        public double CurrentCps { get; private set; } = 0;
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
        }

        public void Start(ClickSettings settings)
        {
            lock (_lock)
            {
                if (State == AutoClickState.Running || State == AutoClickState.CountingDown)
                    return;

                CurrentSettings = settings.Clone();

                // Dispose previous CTS cleanly and instantiate fresh token
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                catch { }

                _cts = new CancellationTokenSource();
                SessionClicksCount = 0;
                CurrentCps = 0;
                ElapsedTime = TimeSpan.Zero;

                LoggerService.Log($"[AUTOCLICK START] Mode={CurrentSettings.PositionMode}, Button={CurrentSettings.MouseButton}, CPS={CurrentSettings.ComputedCps:0.0}, RepeatMode={CurrentSettings.RepeatMode}, RepeatCount={CurrentSettings.RepeatCount}, RepeatDuration={CurrentSettings.RepeatDurationSeconds}s, Thread={Environment.CurrentManagedThreadId}, TokenCancelled={_cts.IsCancellationRequested}");

                _executionTask = Task.Run(() => ExecutionLoopAsync(CurrentSettings, _cts.Token));
            }
        }

        public void Stop(AutoClickStopReason reason = AutoClickStopReason.UserButton)
        {
            lock (_lock)
            {
                if (State == AutoClickState.Stopped) return;

                LoggerService.Log($"[AUTOCLICK STOP] Reason={reason}, Elapsed={ElapsedTime.TotalSeconds:0.0}s, Clicks={SessionClicksCount}, TokenCancelled={_cts?.IsCancellationRequested}");
                
                try
                {
                    _cts?.Cancel();
                }
                catch { }

                State = AutoClickState.Stopped;
                StateChanged?.Invoke(State);
                
                string msg = reason switch
                {
                    AutoClickStopReason.PanicF12 => "Parado pelo botão de emergência",
                    AutoClickStopReason.RepetitionCompleted => "Concluído: limite de cliques atingido",
                    AutoClickStopReason.TimeLimitCompleted => "Concluído: tempo limite atingido",
                    AutoClickStopReason.SafeZone => "Parado: cursor entrou em zona protegida",
                    _ => "Parado"
                };

                StatusMessageUpdated?.Invoke(msg);

                if (reason == AutoClickStopReason.PanicF12)
                {
                    SoundService.PlayPanicSound();
                }
                else
                {
                    SoundService.PlayStopSound();
                }
            }
        }

        public void Panic()
        {
            Stop(AutoClickStopReason.PanicF12);
        }

        private async Task ExecutionLoopAsync(ClickSettings settings, CancellationToken ct)
        {
            var swOverall = Stopwatch.StartNew();
            var swCpsWindow = Stopwatch.StartNew();
            var swRunningLog = Stopwatch.StartNew();
            long clicksInCpsWindow = 0;
            long totalClicksExecuted = 0;

            try
            {
                // 1. Handle Start Delay
                if (settings.StartDelaySeconds > 0)
                {
                    State = AutoClickState.CountingDown;
                    StateChanged?.Invoke(State);

                    for (int s = settings.StartDelaySeconds; s > 0; s--)
                    {
                        if (ct.IsCancellationRequested) return;
                        CountdownProgress?.Invoke(s);
                        StatusMessageUpdated?.Invoke($"Iniciando em {s}...");
                        SoundService.PlayCountdownTick();
                        await Task.Delay(1000, ct);
                    }
                }

                if (ct.IsCancellationRequested) return;

                State = AutoClickState.Running;
                StateChanged?.Invoke(State);
                StatusMessageUpdated?.Invoke("Clicando");
                SoundService.PlayStartSound();

                // Capture initial position if CurrentPosition mode
                int capturedX = 0, capturedY = 0;
                if (settings.PositionMode == ClickPositionMode.CurrentPosition)
                {
                    var pos = _mouseInput.GetCursorPosition();
                    capturedX = pos.X;
                    capturedY = pos.Y;
                    LoggerService.Log($"Captured CurrentPosition for session: ({capturedX}, {capturedY})");
                }

                swOverall.Restart();
                swCpsWindow.Restart();
                swRunningLog.Restart();

                while (!ct.IsCancellationRequested)
                {
                    // Check repeat count limit
                    if (settings.RepeatMode == RepeatMode.Count)
                    {
                        if (settings.RepeatCount > 0 && totalClicksExecuted >= settings.RepeatCount)
                        {
                            LoggerService.Log($"[AUTOCLICK REPEAT LIMIT] Reached {totalClicksExecuted}/{settings.RepeatCount} clicks.");
                            Stop(AutoClickStopReason.RepetitionCompleted);
                            break;
                        }
                    }
                    // Check repeat duration limit
                    else if (settings.RepeatMode == RepeatMode.Duration)
                    {
                        if (settings.RepeatDurationSeconds > 0 && swOverall.Elapsed.TotalSeconds >= settings.RepeatDurationSeconds)
                        {
                            LoggerService.Log($"[AUTOCLICK DURATION LIMIT] Reached {swOverall.Elapsed.TotalSeconds:0.0}/{settings.RepeatDurationSeconds}s.");
                            Stop(AutoClickStopReason.TimeLimitCompleted);
                            break;
                        }
                    }
                    // RepeatMode.UntilStopped (Infinite): NEVER stops automatically!

                    // Check Window Restriction
                    if (!_windowRestrictionService.IsTargetWindowActive())
                    {
                        if (State != AutoClickState.Paused)
                        {
                            State = AutoClickState.Paused;
                            StateChanged?.Invoke(State);
                            StatusMessageUpdated?.Invoke("Pausado: janela alvo não está em foco");
                            LoggerService.Log("AutoClick paused: Target window not foreground");
                        }
                        await Task.Delay(150, ct);
                        continue;
                    }
                    else if (State == AutoClickState.Paused)
                    {
                        State = AutoClickState.Running;
                        StateChanged?.Invoke(State);
                        StatusMessageUpdated?.Invoke("Clicando");
                        LoggerService.Log("AutoClick resumed: Target window returned to foreground");
                    }

                    // Periodic running log every 5s
                    if (swRunningLog.ElapsedMilliseconds >= 5000)
                    {
                        LoggerService.Log($"[AUTOCLICK RUNNING] Elapsed={swOverall.Elapsed.TotalSeconds:0.#}s, Clicks={totalClicksExecuted}, CurrentCPS={CurrentCps:0.0}");
                        swRunningLog.Restart();
                    }

                    // Burst Mode Execution or Standard Execution
                    if (settings.BurstModeEnabled)
                    {
                        for (int i = 0; i < settings.BurstClicksCount && !ct.IsCancellationRequested; i++)
                        {
                            bool clicked = ExecuteSingleClick(settings, ref capturedX, ref capturedY, totalClicksExecuted + 1);
                            if (clicked)
                            {
                                totalClicksExecuted++;
                                clicksInCpsWindow++;
                                SessionClicksCount = totalClicksExecuted;
                                _statisticsService.RegisterClick();
                            }

                            if (i < settings.BurstClicksCount - 1)
                            {
                                MouseInputService.PreciseWaitMilliseconds(settings.BurstIntervalMs, ct);
                            }
                        }

                        // Burst cooldown
                        MouseInputService.PreciseWaitMilliseconds(settings.BurstCooldownMs, ct);
                    }
                    else
                    {
                        bool clicked = ExecuteSingleClick(settings, ref capturedX, ref capturedY, totalClicksExecuted + 1);
                        if (clicked)
                        {
                            totalClicksExecuted++;
                            clicksInCpsWindow++;
                            SessionClicksCount = totalClicksExecuted;
                            _statisticsService.RegisterClick();
                        }

                        // Calculate next interval
                        int delayMs = settings.TotalIntervalMs;
                        if (settings.RandomIntervalEnabled)
                        {
                            int min = Math.Min(settings.MinRandomIntervalMs, settings.MaxRandomIntervalMs);
                            int max = Math.Max(settings.MinRandomIntervalMs, settings.MaxRandomIntervalMs);
                            delayMs = _rng.Next(min, max + 1);
                        }

                        // Enforce safe minimum threshold
                        if (delayMs < 1) delayMs = 1;

                        MouseInputService.PreciseWaitMilliseconds(delayMs, ct);
                    }

                    // Periodic metrics dispatch (every ~200ms)
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
            catch (OperationCanceledException)
            {
                LoggerService.Log("AutoClick loop cancelled cleanly");
            }
            catch (Exception ex)
            {
                LoggerService.LogException("AutoClick ExecutionLoop error", ex);
                Stop(AutoClickStopReason.InternalError);
            }
            finally
            {
                ElapsedTime = swOverall.Elapsed;
                State = AutoClickState.Stopped;
                StateChanged?.Invoke(State);
                StatusMessageUpdated?.Invoke("Parado");
                _statisticsService.SaveStatistics();
                LoggerService.Log($"[AUTOCLICK FINISHED] Total session clicks: {SessionClicksCount}, Duration: {ElapsedTime.TotalSeconds:0.0}s");
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
                    _mouseInput.SetCursorPosition(targetX, targetY);
                    break;

                case ClickPositionMode.FixedPosition:
                    targetX = settings.FixedX;
                    targetY = settings.FixedY;
                    _mouseInput.SetCursorPosition(targetX, targetY);
                    break;
            }

            // Apply Random Position Jitter if enabled
            if (settings.RandomPositionEnabled && settings.RandomRadiusPixels > 0)
            {
                var jittered = _mouseInput.ApplyRandomRadius(targetX, targetY, settings.RandomRadiusPixels);
                targetX = jittered.X;
                targetY = jittered.Y;
                if (settings.PositionMode != ClickPositionMode.FollowCursor)
                {
                    _mouseInput.SetCursorPosition(targetX, targetY);
                }
            }

            // Check Safe Zones
            if (_safeZoneService.IsPointInSafeZone(targetX, targetY, out var hitZone))
            {
                SafeZoneBlocked?.Invoke(targetX, targetY);
                StatusMessageUpdated?.Invoke($"Clique bloqueado pela zona protegida \"{hitZone?.Name}\"");
                LoggerService.Log($"Click blocked by safe zone '{hitZone?.Name}' at ({targetX}, {targetY})");
                return false;
            }

            // Execute mouse action
            bool success = _mouseInput.ExecuteClickAction(settings.MouseButton, settings.ClickAction);
            LoggerService.LogClick(clickSeq, targetX, targetY, success ? 1u : 0u, success ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return success;
        }
    }
}
