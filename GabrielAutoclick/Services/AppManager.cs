using System;
using System.Diagnostics;
using System.Windows;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;
using GabrielAutoclick.Views;
using Application = System.Windows.Application;

namespace GabrielAutoclick.Services
{
    public enum AppExecutionState
    {
        Stopped = 0,
        Running = 1,
        Paused = 2,
        Recording = 3,
        PlayingMacro = 4
    }

    public class AppManager : IDisposable
    {
        private static AppManager? _instance;
        public static AppManager Instance => _instance ??= new AppManager();

        public SettingsService SettingsService { get; }
        public StatisticsService StatisticsService { get; }
        public SafeZoneService SafeZoneService { get; }
        public WindowRestrictionService WindowRestrictionService { get; }
        public MouseInputService MouseInputService { get; }
        public AutoClickService AutoClickService { get; }
        public MultiClickService MultiClickService { get; }
        public MacroService MacroService { get; }
        public BackgroundClickService BackgroundClickService { get; }
        public HotkeyService HotkeyService { get; }
        public ProfileService ProfileService { get; }
        public TrayIconService TrayIconService { get; }

        public MainWindow? MainWindowInstance { get; set; }
        public MiniWindow? MiniWindowInstance { get; set; }

        public AppExecutionState CurrentState { get; private set; } = AppExecutionState.Stopped;

        public event Action<AppExecutionState>? StateChanged;

        private AppManager()
        {
            LoggerService.Initialize();
            LoggerService.Log("Initializing AppManager central services...");

            SettingsService = new SettingsService();
            StatisticsService = new StatisticsService();
            SafeZoneService = new SafeZoneService(SettingsService.CurrentSettings.SafeZones);
            WindowRestrictionService = new WindowRestrictionService();
            MouseInputService = new MouseInputService();
            ProfileService = new ProfileService();

            AutoClickService = new AutoClickService(
                MouseInputService,
                SafeZoneService,
                WindowRestrictionService,
                StatisticsService
            );

            MultiClickService = new MultiClickService(
                MouseInputService,
                SafeZoneService,
                WindowRestrictionService,
                StatisticsService
            );

            MacroService = new MacroService(
                MouseInputService,
                SafeZoneService,
                WindowRestrictionService,
                StatisticsService
            );

            BackgroundClickService = new BackgroundClickService(StatisticsService);

            HotkeyService = new HotkeyService(SettingsService.CurrentSettings.Hotkeys);
            TrayIconService = new TrayIconService();

            WireCentralEvents();
        }

        private void WireCentralEvents()
        {
            // AutoClick State Sync
            AutoClickService.StateChanged += (state) =>
            {
                AppExecutionState appState = state switch
                {
                    AutoClickState.Running => AppExecutionState.Running,
                    AutoClickState.Paused => AppExecutionState.Paused,
                    AutoClickState.CountingDown => AppExecutionState.Running,
                    _ => AppExecutionState.Stopped
                };

                SetExecutionState(appState);
            };

            // Macro State Sync
            MacroService.RecordingStateChanged += (recording) =>
            {
                if (recording) SetExecutionState(AppExecutionState.Recording);
                else SetExecutionState(AppExecutionState.Stopped);
            };

            MacroService.PlaybackStateChanged += (playing) =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (playing) SetExecutionState(AppExecutionState.PlayingMacro);
                    else if (AutoClickService.State == AutoClickState.Stopped) SetExecutionState(AppExecutionState.Stopped);
                });
            };

            // App hotkeys must never end up inside a recorded macro
            MacroService.IsReservedKey = IsHotkeyVirtualKey;

            // Hotkey Triggers
            HotkeyService.ToggleAutoclickTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => ToggleAutoClick(AutoClickStopReason.GlobalHotkey));
            };

            HotkeyService.SelectPositionTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MainWindowInstance?.PickFixedCoordinate();
                });
            };

            HotkeyService.StartMacroTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StartMacro());
            };

            HotkeyService.StopMacroTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopMacro());
            };

            HotkeyService.BackgroundToggleTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => MainWindowInstance?.ToggleBackgroundClick());
            };

            HotkeyService.BackgroundPauseTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => BackgroundClickService.TogglePause());
            };

            HotkeyService.PanicTriggered += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopEverything());
            };

            // Tray Icon Triggers
            TrayIconService.OpenMainWindowRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => ShowMainWindow());
            };

            TrayIconService.ToggleMiniModeRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => ToggleMiniMode());
            };

            TrayIconService.StartAutoClickRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StartAutoClick());
            };

            TrayIconService.StopAutoClickRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopAutoClick());
            };

            TrayIconService.TogglePauseRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => TogglePause());
            };

            TrayIconService.PositionModeChanged += (mode) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MainWindowInstance?.SetPositionMode(mode);
                });
            };

            TrayIconService.MouseButtonChanged += (btn) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MainWindowInstance?.SetMouseButton(btn);
                });
            };

            TrayIconService.CpsPresetSelected += (cps) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MainWindowInstance?.SetCps(cps);
                });
            };

            TrayIconService.StartMacroRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StartMacro());
            };

            TrayIconService.StopMacroRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopMacro());
            };

            TrayIconService.StartMultiClickRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StartMultiClick());
            };

            TrayIconService.StopMultiClickRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopMultiClick());
            };

            TrayIconService.OpenSettingsRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ShowMainWindow();
                    MainWindowInstance?.OpenSettingsTab();
                });
            };

            TrayIconService.PanicRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => StopEverything());
            };

            TrayIconService.ExitRequested += () =>
            {
                Application.Current?.Dispatcher.Invoke(() => ExitApp());
            };
        }

        public void SetExecutionState(AppExecutionState state)
        {
            CurrentState = state;
            HotkeyService.IsRunningAutoclickOrMacro = (state != AppExecutionState.Stopped);

            string tooltip = state switch
            {
                AppExecutionState.Running => $"Clicando ({AutoClickService.CurrentSettings.ComputedCps:0.#} CPS)",
                AppExecutionState.Paused => "Pausado",
                AppExecutionState.Recording => "Gravando macro",
                AppExecutionState.PlayingMacro => "Executando macro",
                _ => "Desligado"
            };

            TrayIconService.UpdateTooltip(tooltip);
            StateChanged?.Invoke(state);
        }

        #region Central Operations

        public void StartAutoClick()
        {
            if (CurrentState == AppExecutionState.Running) return;

            ClickSettings settings;
            if (MainWindowInstance != null)
            {
                settings = MainWindowInstance.GetActiveSettings();
            }
            else if (ProfileService.ActiveProfile != null)
            {
                settings = ProfileService.ActiveProfile.Settings;
            }
            else
            {
                settings = new ClickSettings();
            }

            LoggerService.Log($"[ACTION] StartAutoClick: Mode={settings.PositionMode}, Button={settings.MouseButton}, CPS={settings.ComputedCps}");
            AutoClickService.Start(settings);
        }

        public void StopAutoClick(AutoClickStopReason reason = AutoClickStopReason.UserButton)
        {
            LoggerService.Log($"[ACTION] StopAutoClick Reason={reason}");
            AutoClickService.Stop(reason);
        }

        public void ToggleAutoClick(AutoClickStopReason reason = AutoClickStopReason.GlobalHotkey)
        {
            if (AutoClickService.State == AutoClickState.Running || AutoClickService.State == AutoClickState.CountingDown)
            {
                StopAutoClick(reason);
            }
            else
            {
                StartAutoClick();
            }
        }

        public void TogglePause()
        {
            // Toggle pause/running
            if (AutoClickService.State == AutoClickState.Running)
            {
                AutoClickService.Stop();
            }
            else
            {
                StartAutoClick();
            }
        }

        public void StartMacro()
        {
            // Pressing the play hotkey while recording finishes the recording first
            if (MacroService.IsRecording)
            {
                MacroService.StopRecording();
                return;
            }

            if (MainWindowInstance != null)
            {
                MainWindowInstance.StartMacroPlayback();
            }
            else if (MacroService.RecordedActions.Count > 0)
            {
                MacroService.StartPlayback(1.0, 1);
            }
        }

        public void StopMacro()
        {
            if (MacroService.IsRecording)
            {
                MacroService.StopRecording();
            }
            else
            {
                MacroService.StopPlayback();
            }
        }

        private bool IsHotkeyVirtualKey(int vk)
        {
            if (vk == NativeMethods.VK_F12) return true;
            var cfg = HotkeyService.Config;
            foreach (var item in new[] { cfg.ToggleAutoclick, cfg.SelectPosition, cfg.StartMacro, cfg.StopMacro, cfg.BackgroundToggle, cfg.BackgroundPause })
            {
                if (item != null && item.Key != System.Windows.Input.Key.None &&
                    System.Windows.Input.KeyInterop.VirtualKeyFromKey(item.Key) == vk)
                {
                    return true;
                }
            }
            return false;
        }

        public void StartMultiClick()
        {
            MainWindowInstance?.StartMultiClick();
        }

        public void StopMultiClick()
        {
            MultiClickService.Stop();
        }

        public void StopEverything()
        {
            LoggerService.Log("[ACTION] StopEverything (PANIC TRIGGERED)");
            AutoClickService.Panic();
            MultiClickService.Panic();
            MacroService.Panic();
            BackgroundClickService.Panic();
            SetExecutionState(AppExecutionState.Stopped);
        }

        public void ShowMainWindow()
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (MainWindowInstance == null)
                    {
                        LoggerService.Log("[APP] MainWindowInstance é nula ao chamar ShowMainWindow(). Instanciando nova...");
                        MainWindowInstance = new MainWindow();
                        Application.Current.MainWindow = MainWindowInstance;
                    }

                    if (MiniWindowInstance != null && MiniWindowInstance.IsVisible)
                    {
                        MiniWindowInstance.Hide();
                    }

                    LoggerService.Log("[APP] Exibindo MainWindow...");

                    // Ensure window is in normal state and visible
                    MainWindowInstance.WindowState = WindowState.Normal;
                    MainWindowInstance.ShowInTaskbar = true;
                    MainWindowInstance.Visibility = Visibility.Visible;

                    // Ensure coordinates are within visible screen bounds
                    EnsureWindowOnScreen(MainWindowInstance);

                    MainWindowInstance.Show();
                    MainWindowInstance.Activate();
                    MainWindowInstance.Focus();

                    // Bring to front with Win32
                    var helper = new System.Windows.Interop.WindowInteropHelper(MainWindowInstance);
                    IntPtr handle = helper.EnsureHandle();
                    if (handle != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                    }

                    MainWindowInstance.Topmost = true;
                    MainWindowInstance.Topmost = SettingsService.CurrentSettings.AlwaysOnTop;

                    LoggerService.Log("MainWindow displayed and activated to front");
                }
                catch (Exception ex)
                {
                    LoggerService.LogException("Failed to display MainWindow", ex);
                }
            });
        }

        private static void EnsureWindowOnScreen(Window window)
        {
            try
            {
                double virtualLeft = SystemParameters.VirtualScreenLeft;
                double virtualTop = SystemParameters.VirtualScreenTop;
                double virtualWidth = SystemParameters.VirtualScreenWidth;
                double virtualHeight = SystemParameters.VirtualScreenHeight;

                if (double.IsNaN(window.Left) || double.IsNaN(window.Top) ||
                    window.Left < virtualLeft || window.Left + 150 > virtualLeft + virtualWidth ||
                    window.Top < virtualTop || window.Top + 80 > virtualTop + virtualHeight)
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - (window.Width > 0 ? window.Width : 950)) / 2);
                    window.Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - (window.Height > 0 ? window.Height : 650)) / 2);
                }
            }
            catch { }
        }

        public void ToggleMiniMode()
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (MiniWindowInstance == null)
                {
                    if (MainWindowInstance == null) MainWindowInstance = new MainWindow();
                    MiniWindowInstance = new MiniWindow(AutoClickService, MainWindowInstance);
                }

                if (MiniWindowInstance.IsVisible)
                {
                    MiniWindowInstance.Hide();
                    ShowMainWindow();
                }
                else
                {
                    if (MainWindowInstance != null)
                    {
                        MiniWindowInstance.Left = MainWindowInstance.Left + (MainWindowInstance.ActualWidth - MiniWindowInstance.Width) / 2;
                        MiniWindowInstance.Top = MainWindowInstance.Top + (MainWindowInstance.ActualHeight - MiniWindowInstance.Height) / 2;
                        MainWindowInstance.Hide();
                    }
                    MiniWindowInstance.Show();
                    MiniWindowInstance.Activate();
                }
            });
        }

        public void ExitApp()
        {
            LoggerService.Log("Exiting application cleanly...");
            StopEverything();

            if (MainWindowInstance != null)
            {
                SettingsService.SaveSettings(MainWindowInstance.GetActiveSettingsForSaving());
            }

            StatisticsService.SaveStatistics();
            TrayIconService.Dispose();
            HotkeyService.Dispose();
            MacroService.Dispose();

            Application.Current?.Shutdown();
        }

        #endregion

        public void Dispose()
        {
            TrayIconService.Dispose();
            HotkeyService.Dispose();
            MacroService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
