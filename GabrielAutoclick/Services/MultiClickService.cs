using System;
using System.Collections.Generic;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    /// <summary>
    /// Clicks a list of screen points in order. Lifecycle handled by <see cref="RunController"/>.
    /// </summary>
    public class MultiClickService
    {
        private readonly MouseInputService _mouseInput;
        private readonly SafeZoneService _safeZoneService;
        private readonly WindowRestrictionService _windowRestrictionService;
        private readonly StatisticsService _statisticsService;
        private readonly RunController _runner = new RunController("MultiClick");

        public RunState State => _runner.State;
        public bool IsRunning => _runner.IsActive;
        public bool IsPaused => _runner.State == RunState.Paused;
        public int CurrentPointIndex { get; private set; }
        public int CompletedCycles { get; private set; }

        /// <summary>Kept for compatibility: true while running or paused.</summary>
        public event Action<bool>? RunningStateChanged;
        public event Action<RunState>? StateChanged;
        public event Action<int, int>? PointExecuted; // (pointIndex, totalPoints)
        public event Action<int>? CycleCompleted;
        public event Action<string>? StatusUpdated;

        public MultiClickService(
            MouseInputService mouseInput,
            SafeZoneService safeZoneService,
            WindowRestrictionService windowRestrictionService,
            StatisticsService statisticsService)
        {
            _mouseInput = mouseInput;
            _safeZoneService = safeZoneService;
            _windowRestrictionService = windowRestrictionService;
            _statisticsService = statisticsService;

            _runner.StateChanged += s =>
            {
                StateChanged?.Invoke(s);
                RunningStateChanged?.Invoke(s is RunState.Running or RunState.Paused);
            };
        }

        public void Start(List<ClickPoint> points, int loopCount = 0) // 0 = infinite
        {
            if (points == null || points.Count == 0 || _runner.IsActive) return;

            CompletedCycles = 0;
            CurrentPointIndex = 0;
            var copy = new List<ClickPoint>(points);

            if (_runner.TryStart(ctx => RunLoop(ctx, copy, loopCount)))
            {
                SoundService.PlayStartSound();
            }
        }

        public void Stop()
        {
            if (!_runner.IsActive) return;
            _runner.Stop();
            StatusUpdated?.Invoke("Sequência parada.");
            SoundService.PlayStopSound();
        }

        public void TogglePause()
        {
            if (_runner.State == RunState.Paused)
            {
                if (_runner.Resume()) StatusUpdated?.Invoke("Executando sequência...");
            }
            else if (_runner.Pause())
            {
                StatusUpdated?.Invoke("Sequência pausada.");
            }
        }

        public void Panic()
        {
            if (!_runner.IsActive) return;
            _runner.Stop();
            StatusUpdated?.Invoke("Parado pelo botão de emergência.");
            SoundService.PlayPanicSound();
        }

        private void RunLoop(RunContext ctx, List<ClickPoint> points, int maxLoops)
        {
            StatusUpdated?.Invoke("Executando sequência...");

            while (ctx.WaitIfPaused())
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (!ctx.WaitIfPaused()) return;

                    // Optional focus restriction: wait, never stop
                    bool waited = false;
                    while (!_windowRestrictionService.IsTargetWindowActive())
                    {
                        if (!waited) StatusUpdated?.Invoke("Aguardando a janela alvo voltar ao foco.");
                        waited = true;
                        if (!ctx.Sleep(200)) return;
                    }
                    if (waited) StatusUpdated?.Invoke("Executando sequência...");

                    var pt = points[i];
                    CurrentPointIndex = i;
                    PointExecuted?.Invoke(i + 1, points.Count);

                    // Includes the "never click on Gabriel Autoclick itself" rule
                    if (!_safeZoneService.IsPointInSafeZone(pt.X, pt.Y, out var zone))
                    {
                        _mouseInput.SetCursorPosition(pt.X, pt.Y);
                        _mouseInput.ExecuteClickAction(pt.MouseButton, pt.ClickAction);
                        _statisticsService.RegisterClick();
                    }
                    else
                    {
                        StatusUpdated?.Invoke(zone == SafeZoneService.OwnWindowZone
                            ? $"Ponto {i + 1} está sobre o Gabriel Autoclick; clique pulado."
                            : $"Ponto {i + 1} bloqueado por zona protegida.");
                    }

                    if (!ctx.PreciseSleep(Math.Max(pt.DelayMs, 1))) return;
                }

                CompletedCycles++;
                CycleCompleted?.Invoke(CompletedCycles);

                if (maxLoops > 0 && CompletedCycles >= maxLoops)
                {
                    StatusUpdated?.Invoke($"Sequência concluída ({CompletedCycles} ciclo(s)).");
                    SoundService.PlayStopSound();
                    return;
                }
            }
        }
    }
}
