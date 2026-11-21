using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class MultiClickService
    {
        private readonly MouseInputService _mouseInput;
        private readonly SafeZoneService _safeZoneService;
        private readonly WindowRestrictionService _windowRestrictionService;
        private readonly StatisticsService _statisticsService;

        private CancellationTokenSource? _cts;
        private Task? _executionTask;
        private readonly object _lock = new object();

        public bool IsRunning { get; private set; } = false;
        public int CurrentPointIndex { get; private set; } = 0;
        public int CompletedCycles { get; private set; } = 0;

        public event Action<bool>? RunningStateChanged;
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
        }

        public void Start(List<ClickPoint> points, int loopCount = 0) // 0 = infinite
        {
            lock (_lock)
            {
                if (IsRunning || points == null || points.Count == 0) return;

                _cts = new CancellationTokenSource();
                IsRunning = true;
                CompletedCycles = 0;
                CurrentPointIndex = 0;
                RunningStateChanged?.Invoke(true);
                SoundService.PlayStartSound();

                var copyList = new List<ClickPoint>(points);
                _executionTask = Task.Run(() => ExecutionLoopAsync(copyList, loopCount, _cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                _cts?.Cancel();
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
                StatusUpdated?.Invoke("Sequência parada.");
                SoundService.PlayStopSound();
            }
        }

        public void Panic()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
                StatusUpdated?.Invoke("Parado pelo botão de emergência.");
                SoundService.PlayPanicSound();
            }
        }

        private async Task ExecutionLoopAsync(List<ClickPoint> points, int maxLoops, CancellationToken ct)
        {
            try
            {
                StatusUpdated?.Invoke("Executando sequência...");

                while (!ct.IsCancellationRequested)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (ct.IsCancellationRequested) return;

                        // Check Window Restriction
                        while (!_windowRestrictionService.IsTargetWindowActive())
                        {
                            StatusUpdated?.Invoke("Pausado: janela alvo não está em foco.");
                            await Task.Delay(200, ct);
                            if (ct.IsCancellationRequested) return;
                        }

                        var pt = points[i];
                        CurrentPointIndex = i;
                        PointExecuted?.Invoke(i + 1, points.Count);

                        // Check Safe Zone
                        if (!_safeZoneService.IsPointInSafeZone(pt.X, pt.Y, out _))
                        {
                            _mouseInput.SetCursorPosition(pt.X, pt.Y);
                            _mouseInput.ExecuteClickAction(pt.MouseButton, pt.ClickAction);
                            _statisticsService.RegisterClick();
                        }
                        else
                        {
                            StatusUpdated?.Invoke($"Ponto {i + 1} bloqueado por zona protegida.");
                        }

                        // Wait point delay
                        int delay = Math.Max(pt.DelayMs, 1);
                        MouseInputService.PreciseWaitMilliseconds(delay, ct);
                    }

                    CompletedCycles++;
                    CycleCompleted?.Invoke(CompletedCycles);

                    if (maxLoops > 0 && CompletedCycles >= maxLoops)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"MultiClick error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
                StatusUpdated?.Invoke("Sequência concluída.");
            }
        }
    }
}
