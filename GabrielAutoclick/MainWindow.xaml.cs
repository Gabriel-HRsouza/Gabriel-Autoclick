using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;
using GabrielAutoclick.Services;
using GabrielAutoclick.Views;
using Microsoft.Win32;

// WPF Disambiguation Aliases
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using ListBox = System.Windows.Controls.ListBox;
using DataGrid = System.Windows.Controls.DataGrid;
using UserControl = System.Windows.Controls.UserControl;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;

namespace GabrielAutoclick
{
    public partial class MainWindow : Window
    {
        // Central App Manager Reference
        private readonly AppManager _appManager = AppManager.Instance;

        private SettingsService _settingsService => _appManager.SettingsService;
        private StatisticsService _statisticsService => _appManager.StatisticsService;
        private ProfileService _profileService => _appManager.ProfileService;
        private SafeZoneService _safeZoneService => _appManager.SafeZoneService;
        private WindowRestrictionService _windowRestrictionService => _appManager.WindowRestrictionService;
        private MouseInputService _mouseInputService => _appManager.MouseInputService;
        private AutoClickService _autoClickService => _appManager.AutoClickService;
        private MultiClickService _multiClickService => _appManager.MultiClickService;
        private MacroService _macroService => _appManager.MacroService;
        private HotkeyService _hotkeyService => _appManager.HotkeyService;

        // Observable collections for DataGrids / ListViews
        private readonly ObservableCollection<ClickPoint> _multiPoints = new ObservableCollection<ClickPoint>();
        private readonly ObservableCollection<MacroAction> _macroActions = new ObservableCollection<MacroAction>();
        private readonly ObservableCollection<SafeZone> _safeZones = new ObservableCollection<SafeZone>();

        // Internal flags to prevent recursive updates
        private bool _isUpdatingInputs = false;
        private bool _isClosingFromTray = false;
        private bool _isInitialized = false;

        public MainWindow()
        {
            _appManager.MainWindowInstance = this;

            LoggerService.Log("[STARTUP] InitializeComponent iniciado");
            InitializeComponent();
            LoggerService.Log("[STARTUP] InitializeComponent concluído");

            // Wire DataGrids
            DgMultiPoints.ItemsSource = _multiPoints;
            DgMacroActions.ItemsSource = _macroActions;
            DgSafeZones.ItemsSource = _safeZones;

            // Load initial safe zones
            foreach (var sz in _settingsService.CurrentSettings.SafeZones)
            {
                _safeZones.Add(sz);
            }

            // Wire Service Events
            WireServiceEvents();

            // Load initial settings & profile into UI
            LoadSettingsToUI(_settingsService.CurrentSettings);
            LoadProfileListToUI();
            RefreshOpenWindowsList();

            // Sync Sound Service toggles
            SoundService.StartSoundEnabled = _settingsService.CurrentSettings.PlaySoundStart;
            SoundService.StopSoundEnabled = _settingsService.CurrentSettings.PlaySoundStop;
            System.Threading.Tasks.Task.Run(SoundService.Preload);

            TxtVersion.Text = $"{Constants.AppName}  ·  versão {Constants.AppVersion}";
            UpdateMacroSummary();

            InitBackgroundClick();

            _isInitialized = true;
            LoggerService.Log("[STARTUP] MainWindow pronta e visível");
        }

        #region Service Event Wiring

        private void WireServiceEvents()
        {
            // AutoClick Events
            _autoClickService.StateChanged += OnAutoClickStateChanged;
            _autoClickService.MetricsUpdated += OnMetricsUpdated;
            _autoClickService.CountdownProgress += OnCountdownProgress;
            _autoClickService.StatusMessageUpdated += OnStatusMessageUpdated;
            _autoClickService.SafeZoneBlocked += (x, y) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtDashSubstatus.Text = $"Clique bloqueado em ({x}, {y}) por uma zona protegida.";
                });
            };

            // MultiClick Events
            _multiClickService.StateChanged += (state) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    bool running = state is RunState.Running or RunState.Paused;
                    _hotkeyService.IsRunningAutoclickOrMacro = running;
                    TxtMultiIcon.Text = running ? "\uE71A" : "\uE768";
                    TxtMultiText.Text = running ? "Parar sequência" : "Iniciar sequência";
                    BtnToggleMulti.Style = (Style)FindResource(running ? "SolidDangerButton" : "PrimaryButton");
                    BtnPauseMulti.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
                    TxtMultiPauseIcon.Text = state == RunState.Paused ? "\uE768" : "\uE769";
                    TxtMultiPauseText.Text = state == RunState.Paused ? "Continuar" : "Pausar";
                    UpdateVisualStatus(_autoClickService.State);
                });
            };

            _multiClickService.PointExecuted += (cur, total) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TxtMultiStatus.Text = $"Executando ponto {cur} de {total}";
                });
            };

            _multiClickService.StatusUpdated += (msg) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TxtMultiStatus.Text = msg;
                });
            };

            // Macro Events (playback events arrive from a background thread)
            _macroService.RecordingStateChanged += (recording) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _hotkeyService.IsRunningAutoclickOrMacro = recording;
                    BtnRecordMacro.IsEnabled = !recording;
                    BtnStopRecordMacro.IsEnabled = recording || _macroService.IsPlaying;
                    BtnPlayMacro.IsEnabled = !recording;
                    BtnClearMacro.IsEnabled = !recording;
                    ChkMacroRecordMoves.IsEnabled = !recording;
                    ChkMacroRecordKeyboard.IsEnabled = !recording;

                    if (recording)
                    {
                        SetMacroStatus("Gravando. Use o mouse e o teclado normalmente; F9 ou Parar finaliza.", "DangerBrush");
                        SetGlobalStatus("Gravando macro", "DangerBrush");
                    }
                    else
                    {
                        // Recording finished: show the cleaned-up list from the service
                        _macroActions.Clear();
                        foreach (var act in _macroService.RecordedActions) _macroActions.Add(act);
                        UpdateMacroSummary();
                        SetMacroStatus(_macroActions.Count > 0 ? "Gravação concluída" : "Nada foi gravado", "TextMutedBrush");
                        UpdateVisualStatus(_autoClickService.State);
                    }
                });
            };

            _macroService.ActionRecorded += (act) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_macroService.IsRecording) return;
                    _macroActions.Add(act);
                    TxtMacroSummary.Text = $"{_macroActions.Count} ações gravadas";
                    DgMacroActions.ScrollIntoView(act);
                });
            };

            _macroService.PlaybackRunStateChanged += (state) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    bool playing = state is RunState.Running or RunState.Paused;
                    _hotkeyService.IsRunningAutoclickOrMacro = playing;
                    BtnPlayMacro.IsEnabled = !playing;
                    BtnRecordMacro.IsEnabled = !playing;
                    BtnClearMacro.IsEnabled = !playing;
                    BtnStopRecordMacro.IsEnabled = playing || _macroService.IsRecording;
                    BtnPauseMacro.IsEnabled = playing;
                    TxtMacroPauseIcon.Text = state == RunState.Paused ? "\uE768" : "\uE769";
                    TxtMacroPauseText.Text = state == RunState.Paused ? "Continuar" : "Pausar";

                    switch (state)
                    {
                        case RunState.Running:
                            SetMacroStatus("Reproduzindo", "SuccessBrush");
                            break;
                        case RunState.Paused:
                            SetMacroStatus("Pausada", "WarningBrush");
                            break;
                        case RunState.Finished:
                            SetMacroStatus("Concluída", "TextMutedBrush");
                            break;
                        default:
                            SetMacroStatus("Parada", "TextMutedBrush");
                            break;
                    }
                    UpdateVisualStatus(_autoClickService.State);
                });
            };

            _macroService.PlaybackProgress += (cur, total) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_macroService.IsPlaying) TxtMacroStatus.Text = $"Reproduzindo ação {cur} de {total}";
                });
            };

            _macroService.PlaybackCycleCompleted += (cycles) =>
            {
                Dispatcher.BeginInvoke(() => TxtMacroSummary.Text = $"{_macroActions.Count} ações  ·  {cycles} ciclo(s) concluído(s)");
            };

            // Statistics Events
            _statisticsService.StatisticsUpdated += (stats) =>
            {
                Dispatcher.Invoke(() => UpdateStatisticsUI(stats));
            };

            // Hotkey Event for position picker (others handled in AppManager)
            _hotkeyService.SelectPositionTriggered += () =>
            {
                Dispatcher.Invoke(() => PickFixedCoordinate());
            };

            // Hotkey Capture Event for dynamic shortcut configuration
            _hotkeyService.HotkeyCaptured += (key, mods) => OnHotkeyCapturedFromService(key, mods);
        }

        #endregion

        #region Navigation & Window Controls

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            // A maximized WindowChrome window overhangs the screen by the resize border
            bool max = WindowState == WindowState.Maximized;
            RootBorder.BorderThickness = max ? new Thickness(0) : new Thickness(1);
            RootBorder.Padding = max ? new Thickness(7) : new Thickness(0);
            BtnMaximize.Content = max ? "\uE923" : "\uE922";
            BtnMaximize.ToolTip = max ? "Restaurar" : "Maximizar";
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsService.CurrentSettings.MinimizeToTray)
            {
                this.Hide();
                _appManager.TrayIconService.ShowFirstTimeMinimizeTip();
            }
            else
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsService.CurrentSettings.MinimizeToTray)
            {
                this.Hide();
                _appManager.TrayIconService.ShowFirstTimeMinimizeTip();
            }
            else
            {
                CloseApp();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosingFromTray && _settingsService.CurrentSettings.MinimizeToTray)
            {
                e.Cancel = true;
                this.Hide();
                _appManager.TrayIconService.ShowFirstTimeMinimizeTip();
                LoggerService.Log("MainWindow hidden to System Tray on close click");
            }
            else
            {
                _appManager.ExitApp();
            }
        }

        public void CloseApp()
        {
            _isClosingFromTray = true;
            _appManager.ExitApp();
        }

        private void BtnMiniMode_Click(object sender, RoutedEventArgs e)
        {
            _appManager.ToggleMiniMode();
        }

        public void OpenSettingsTab()
        {
            TabNavSettings.IsChecked = true;
        }

        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && ViewDashboard != null)
            {
                var navItems = new[] { TabNavDashboard, TabNavAutoclick, TabNavMultiClick, TabNavMacro, TabNavSafeZones, TabNavProfiles, TabNavStatistics, TabNavHotkeys, TabNavSettings, TabNavBackground };
                int index = Array.IndexOf(navItems, rb);
                if (index < 0) return;

                ViewDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
                ViewAutoclick.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
                ViewMultiClick.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
                ViewMacro.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
                ViewSafeZones.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
                ViewProfiles.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
                ViewStatistics.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
                ViewHotkeys.Visibility = index == 7 ? Visibility.Visible : Visibility.Collapsed;
                ViewSettings.Visibility = index == 8 ? Visibility.Visible : Visibility.Collapsed;
                ViewBackground.Visibility = index == 9 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        #endregion

        #region Autoclick Execution & Dashboard Toggle

        public ClickSettings GetActiveSettings()
        {
            var s = new ClickSettings();

            int.TryParse(TxtHours.Text, out int h);
            int.TryParse(TxtMinutes.Text, out int m);
            int.TryParse(TxtSeconds.Text, out int sec);
            int.TryParse(TxtMilliseconds.Text, out int ms);

            s.Hours = Math.Max(0, h);
            s.Minutes = Math.Max(0, m);
            s.Seconds = Math.Max(0, sec);
            s.Milliseconds = Math.Max(0, ms);

            // Minimum interval protection
            int minSafe = _settingsService.CurrentSettings.SafeIntervalMinMs;
            if (s.TotalIntervalMs < minSafe)
            {
                s.Milliseconds = minSafe;
            }

            // Mouse button
            if (RbBtnRight.IsChecked == true) s.MouseButton = MouseButtonType.Right;
            else if (RbBtnMiddle.IsChecked == true) s.MouseButton = MouseButtonType.Middle;
            else s.MouseButton = MouseButtonType.Left;

            // Action
            if (RbActionDouble.IsChecked == true) s.ClickAction = ClickActionType.Double;
            else if (RbActionHoldDown.IsChecked == true) s.ClickAction = ClickActionType.HoldDown;
            else if (RbActionHoldUp.IsChecked == true) s.ClickAction = ClickActionType.HoldUp;
            else s.ClickAction = ClickActionType.Single;

            // Position mode
            if (RbPosCurrent.IsChecked == true) s.PositionMode = ClickPositionMode.CurrentPosition;
            else if (RbPosFixed.IsChecked == true)
            {
                s.PositionMode = ClickPositionMode.FixedPosition;
                int.TryParse(TxtFixedX.Text, out int fx);
                int.TryParse(TxtFixedY.Text, out int fy);
                s.FixedX = fx;
                s.FixedY = fy;
            }
            else
            {
                s.PositionMode = ClickPositionMode.FollowCursor;
            }

            // Repetition
            if (RbRepeatCount.IsChecked == true)
            {
                s.RepeatMode = RepeatMode.Count;
                int.TryParse(TxtRepeatCount.Text, out int rc);
                s.RepeatCount = Math.Max(1, rc);
            }
            else if (RbRepeatDuration.IsChecked == true)
            {
                s.RepeatMode = RepeatMode.Duration;
                int.TryParse(TxtRepeatDuration.Text, out int rd);
                s.RepeatDurationSeconds = Math.Max(1, rd);
            }
            else
            {
                s.RepeatMode = RepeatMode.UntilStopped;
            }

            // Burst mode
            s.BurstModeEnabled = ChkBurstMode.IsChecked == true;
            int.TryParse(TxtBurstCount.Text, out int bc);
            int.TryParse(TxtBurstInterval.Text, out int bi);
            int.TryParse(TxtBurstCooldown.Text, out int bcd);
            s.BurstClicksCount = Math.Max(1, bc);
            s.BurstIntervalMs = Math.Max(1, bi);
            s.BurstCooldownMs = Math.Max(1, bcd);

            // Randomization
            s.RandomIntervalEnabled = ChkRandomInterval.IsChecked == true;
            int.TryParse(TxtMinRandomMs.Text, out int rmin);
            int.TryParse(TxtMaxRandomMs.Text, out int rmax);
            s.MinRandomIntervalMs = Math.Max(1, rmin);
            s.MaxRandomIntervalMs = Math.Max(s.MinRandomIntervalMs, rmax);

            s.RandomPositionEnabled = ChkRandomPos.IsChecked == true;
            int.TryParse(TxtRandomRadius.Text, out int rrad);
            s.RandomRadiusPixels = Math.Max(0, rrad);

            // Start delay
            if (CmbStartDelay.SelectedItem is ComboBoxItem selItem && selItem.Tag is string tag && int.TryParse(tag, out int sd))
            {
                s.StartDelaySeconds = sd;
            }

            return s;
        }

        /// <summary>
        /// True when the click being handled was generated by one of our own automations (e.g. the
        /// autoclicker's cursor ended up over this button). Such clicks must never control the app.
        /// </summary>
        private static bool IgnoreSelfClick(string control)
        {
            if (!SelfClickGuard.IsCurrentInputSynthetic()) return false;
            LoggerService.Log($"[GUARD] Clique automático no próprio controle '{control}' ignorado");
            return true;
        }

        private void BtnMainToggle_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Iniciar/Parar autoclick")) return;
            _appManager.ToggleAutoClick(AutoClickStopReason.UserButton);
        }

        private void BtnMainPause_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Pausar autoclick")) return;
            _autoClickService.TogglePause();
        }

        private void BtnPanic_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Parar tudo")) return;
            _appManager.StopEverything();
            TxtDashSubstatus.Text = "Todas as automações foram interrompidas.";
        }

        private void OnAutoClickStateChanged(AutoClickState state)
        {
            // BeginInvoke: the clicking thread never waits for the UI (a busy or minimized window can't slow it down)
            Dispatcher.BeginInvoke(() =>
            {
                _hotkeyService.IsRunningAutoclickOrMacro = state is AutoClickState.Running or AutoClickState.CountingDown or AutoClickState.Paused;
                UpdateVisualStatus(state);
            });
        }

        private void OnMetricsUpdated(long clicks, double cps, TimeSpan elapsed)
        {
            Dispatcher.BeginInvoke(() =>
            {
                string timeStr = elapsed.ToString(@"hh\:mm\:ss");
                TxtTopClicks.Text = clicks.ToString("N0");
                TxtTopCps.Text = cps.ToString("0.0");
                TxtTopTimer.Text = timeStr;

                TxtCardClicks.Text = clicks.ToString("N0");
                TxtCardCps.Text = cps.ToString("0.0");
                TxtCardTime.Text = timeStr;
            });
        }

        private void OnCountdownProgress(int second)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TxtDashSubstatus.Text = $"Iniciando em {second}...";
            });
        }

        private void OnStatusMessageUpdated(string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TxtDashSubstatus.Text = message;
            });
        }

        private void UpdateVisualStatus(AutoClickState state)
        {
            bool active = state is AutoClickState.Running or AutoClickState.Paused or AutoClickState.CountingDown;
            bool userPaused = _autoClickService.IsUserPaused;
            string text;
            string brushKey;

            switch (state)
            {
                case AutoClickState.Running:
                    text = "Clicando";
                    brushKey = "SuccessBrush";
                    break;
                case AutoClickState.Paused:
                    text = userPaused ? "Pausado" : "Aguardando janela";
                    brushKey = "WarningBrush";
                    break;
                case AutoClickState.Finished:
                    text = "Concluído";
                    brushKey = "AccentBrush";
                    break;
                case AutoClickState.CountingDown:
                    text = "Aguardando início";
                    brushKey = "AccentBrush";
                    break;
                default:
                    text = "Desligado";
                    brushKey = "TextMutedBrush";
                    break;
            }

            TxtDashStatus.Text = text;
            DashStatusDot.Fill = (Brush)FindResource(brushKey);

            // Macro activity has priority in the global status bar
            if (_macroService.IsRecording) SetGlobalStatus("Gravando macro", "DangerBrush");
            else if (_macroService.IsPlaying) SetGlobalStatus("Executando macro", "SuccessBrush");
            else if (!active && _multiClickService.State == RunState.Running) SetGlobalStatus("Sequência de pontos ativa", "SuccessBrush");
            else if (!active && _multiClickService.State == RunState.Paused) SetGlobalStatus("Sequência de pontos pausada", "WarningBrush");
            else if (!active && _appManager.BackgroundClickService.State == BackgroundRunState.Running) SetGlobalStatus("Clique em segundo plano ativo", "SuccessBrush");
            else if (!active && _appManager.BackgroundClickService.State == BackgroundRunState.Paused) SetGlobalStatus("Segundo plano pausado", "WarningBrush");
            else SetGlobalStatus(text, brushKey);

            TxtMainActionIcon.Text = active ? "\uE71A" : "\uE768";
            TxtMainActionText.Text = state == AutoClickState.CountingDown ? "Cancelar" : active ? "Parar autoclick" : "Iniciar autoclick";
            BtnMainToggle.Style = (Style)FindResource(active ? "SolidDangerButton" : "PrimaryButton");

            BtnMainPause.Visibility = active && state != AutoClickState.CountingDown ? Visibility.Visible : Visibility.Collapsed;
            TxtMainPauseIcon.Text = userPaused ? "\uE768" : "\uE769";
            TxtMainPauseText.Text = userPaused ? "Continuar" : "Pausar";
        }

        private void SetGlobalStatus(string text, string brushKey)
        {
            TxtTopStatus.Text = text;
            TopStatusDot.Fill = (Brush)FindResource(brushKey);
        }

        private void SetMacroStatus(string text, string brushKey)
        {
            TxtMacroStatus.Text = text;
            MacroStatusDot.Fill = (Brush)FindResource(brushKey);
        }

        private void UpdateMacroSummary()
        {
            if (_macroActions.Count == 0)
            {
                TxtMacroSummary.Text = "Nenhuma ação gravada. Clique em Gravar para começar.";
                return;
            }

            long totalMs = _macroActions.Sum(a => (long)a.DelayMs);
            int clicks = _macroActions.Count(a => a.EventType == MacroEventType.MouseDown || a.EventType == MacroEventType.Click);
            int keys = _macroActions.Count(a => a.EventType == MacroEventType.KeyDown);
            TxtMacroSummary.Text = $"{_macroActions.Count} ações  ·  {clicks} cliques  ·  {keys} teclas  ·  duração de {totalMs / 1000.0:0.0} s";
        }

        #endregion

        #region Dashboard Quick Controls & Synchronization

        public void SetPositionMode(ClickPositionMode mode)
        {
            switch (mode)
            {
                case ClickPositionMode.FollowCursor:
                    RbPosFollow.IsChecked = true;
                    if (CmbDashMode != null) CmbDashMode.SelectedIndex = 0;
                    break;
                case ClickPositionMode.CurrentPosition:
                    RbPosCurrent.IsChecked = true;
                    if (CmbDashMode != null) CmbDashMode.SelectedIndex = 1;
                    break;
                case ClickPositionMode.FixedPosition:
                    RbPosFixed.IsChecked = true;
                    if (CmbDashMode != null) CmbDashMode.SelectedIndex = 2;
                    break;
            }
        }

        public void SetMouseButton(MouseButtonType button)
        {
            switch (button)
            {
                case MouseButtonType.Left:
                    RbBtnLeft.IsChecked = true;
                    if (CmbDashButton != null) CmbDashButton.SelectedIndex = 0;
                    break;
                case MouseButtonType.Right:
                    RbBtnRight.IsChecked = true;
                    if (CmbDashButton != null) CmbDashButton.SelectedIndex = 1;
                    break;
                case MouseButtonType.Middle:
                    RbBtnMiddle.IsChecked = true;
                    if (CmbDashButton != null) CmbDashButton.SelectedIndex = 2;
                    break;
            }
        }

        public void SetCps(double cps)
        {
            if (cps < 0.1) cps = 0.1;
            TxtDirectCps.Text = cps.ToString("0.0");
            if (TxtDashCpsInput != null) TxtDashCpsInput.Text = cps.ToString("0.0");
        }

        private void CmbDashMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || CmbDashMode == null || RbPosFollow == null || RbPosCurrent == null || RbPosFixed == null) return;
            if (CmbDashMode.SelectedIndex == 0) RbPosFollow.IsChecked = true;
            else if (CmbDashMode.SelectedIndex == 1) RbPosCurrent.IsChecked = true;
            else if (CmbDashMode.SelectedIndex == 2) RbPosFixed.IsChecked = true;
        }

        private void CmbDashButton_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || CmbDashButton == null || RbBtnLeft == null || RbBtnRight == null || RbBtnMiddle == null) return;
            if (CmbDashButton.SelectedIndex == 0) RbBtnLeft.IsChecked = true;
            else if (CmbDashButton.SelectedIndex == 1) RbBtnRight.IsChecked = true;
            else if (CmbDashButton.SelectedIndex == 2) RbBtnMiddle.IsChecked = true;
        }

        private void TxtDashCpsInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || TxtDashCpsInput == null || TxtDirectCps == null) return;
            if (double.TryParse(TxtDashCpsInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cps) && cps > 0)
            {
                _isUpdatingInputs = true;
                TxtDirectCps.Text = cps.ToString("0.0");
                SyncCpsToMs(cps);
                _isUpdatingInputs = false;
            }
        }

        private void BtnCpsMinus_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || TxtDashCpsInput == null) return;
            if (double.TryParse(TxtDashCpsInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cps))
            {
                cps = Math.Max(1, cps - 1);
                TxtDashCpsInput.Text = cps.ToString("0");
            }
        }

        private void BtnCpsPlus_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || TxtDashCpsInput == null) return;
            if (double.TryParse(TxtDashCpsInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cps))
            {
                cps = Math.Min(200, cps + 1);
                TxtDashCpsInput.Text = cps.ToString("0");
            }
        }

        private void CmbDashRepeat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || CmbDashRepeat == null || RbRepeatInfinite == null || RbRepeatCount == null || RbRepeatDuration == null) return;
            if (CmbDashRepeat.SelectedIndex == 0) RbRepeatInfinite.IsChecked = true;
            else if (CmbDashRepeat.SelectedIndex == 1)
            {
                RbRepeatCount.IsChecked = true;
                if (TxtRepeatCount != null) TxtRepeatCount.Text = "100";
            }
            else if (CmbDashRepeat.SelectedIndex == 2)
            {
                RbRepeatDuration.IsChecked = true;
                if (TxtRepeatDuration != null) TxtRepeatDuration.Text = "10";
            }
        }

        #endregion

        #region Interval & CPS Two-Way Calculation

        private void IntervalInputs_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || TxtHours == null || TxtMinutes == null || TxtSeconds == null || TxtMilliseconds == null || TxtDirectCps == null) return;
            _isUpdatingInputs = true;

            int.TryParse(TxtHours.Text, out int h);
            int.TryParse(TxtMinutes.Text, out int m);
            int.TryParse(TxtSeconds.Text, out int s);
            int.TryParse(TxtMilliseconds.Text, out int ms);

            long totalMs = ((long)h * 3600000) + ((long)m * 60000) + ((long)s * 1000) + ms;
            if (totalMs > 0)
            {
                double cps = Math.Round(1000.0 / totalMs, 1);
                TxtDirectCps.Text = cps.ToString("0.0");
                if (TxtCardCps != null) TxtCardCps.Text = cps.ToString("0.0");
                if (TxtDashCpsInput != null) TxtDashCpsInput.Text = cps.ToString("0.0");
                if (TxtDashIntervalPreview != null) TxtDashIntervalPreview.Text = $"Intervalo de {totalMs} ms";
            }

            _isUpdatingInputs = false;
        }

        private void TxtDirectCps_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || TxtDirectCps == null) return;
            if (double.TryParse(TxtDirectCps.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cps) && cps > 0)
            {
                _isUpdatingInputs = true;
                SyncCpsToMs(cps);
                if (TxtDashCpsInput != null) TxtDashCpsInput.Text = cps.ToString("0.0");
                _isUpdatingInputs = false;
            }
        }

        private void SyncCpsToMs(double cps)
        {
            int totalMs = (int)Math.Round(1000.0 / cps);
            if (totalMs < 1) totalMs = 1;

            int h = totalMs / 3600000;
            totalMs %= 3600000;
            int m = totalMs / 60000;
            totalMs %= 60000;
            int s = totalMs / 1000;
            int ms = totalMs % 1000;

            if (TxtHours != null) TxtHours.Text = h.ToString();
            if (TxtMinutes != null) TxtMinutes.Text = m.ToString();
            if (TxtSeconds != null) TxtSeconds.Text = s.ToString();
            if (TxtMilliseconds != null) TxtMilliseconds.Text = ms.ToString();

            if (TxtCardCps != null) TxtCardCps.Text = cps.ToString("0.0");
            if (TxtDashIntervalPreview != null)
            {
                int fullMs = (h * 3600000) + (m * 60000) + (s * 1000) + ms;
                TxtDashIntervalPreview.Text = $"Intervalo de {fullMs} ms";
            }
        }

        private void QuickCps_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && double.TryParse(tag, out double cps))
            {
                SetCps(cps);
            }
        }

        #endregion

        #region Position Mode & Screen Overlay Pickers

        private void PositionMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || FixedCoordsBox == null) return;

            if (RbPosFixed.IsChecked == true)
            {
                FixedCoordsBox.Visibility = Visibility.Visible;
                if (CmbDashMode != null) CmbDashMode.SelectedIndex = 2;
            }
            else if (RbPosCurrent.IsChecked == true)
            {
                FixedCoordsBox.Visibility = Visibility.Collapsed;
                if (CmbDashMode != null) CmbDashMode.SelectedIndex = 1;
            }
            else
            {
                FixedCoordsBox.Visibility = Visibility.Collapsed;
                if (CmbDashMode != null) CmbDashMode.SelectedIndex = 0;
            }
        }

        private void MouseButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingInputs || CmbDashButton == null) return;
            if (RbBtnRight.IsChecked == true) CmbDashButton.SelectedIndex = 1;
            else if (RbBtnMiddle.IsChecked == true) CmbDashButton.SelectedIndex = 2;
            else CmbDashButton.SelectedIndex = 0;
        }

        private void ClickAction_Checked(object sender, RoutedEventArgs e) { }
        private void RepeatMode_Checked(object sender, RoutedEventArgs e) { }
        private void ChkBurstMode_Changed(object sender, RoutedEventArgs e) { }
        private void ChkRandomInterval_Changed(object sender, RoutedEventArgs e) { }
        private void ChkRandomPos_Changed(object sender, RoutedEventArgs e) { }
        private void CmbStartDelay_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void BtnPickFixedPoint_Click(object sender, RoutedEventArgs e)
        {
            PickFixedCoordinate();
        }

        public void PickFixedCoordinate()
        {
            var overlay = new PointPickerOverlay();
            overlay.ShowDialog();
            if (overlay.IsConfirmed)
            {
                TxtFixedX.Text = overlay.SelectedX.ToString();
                TxtFixedY.Text = overlay.SelectedY.ToString();
                RbPosFixed.IsChecked = true;
            }
        }

        #endregion

        #region Multi Point Clicker

        private void BtnAddMultiPoint_Click(object sender, RoutedEventArgs e)
        {
            var curPos = _mouseInputService.GetCursorPosition();
            var pt = new ClickPoint
            {
                Index = _multiPoints.Count + 1,
                X = curPos.X,
                Y = curPos.Y,
                DelayMs = 500,
                MouseButton = MouseButtonType.Left,
                ClickAction = ClickActionType.Single
            };
            _multiPoints.Add(pt);
            UpdateMultiStatus();
        }

        private void BtnPickMultiPoint_Click(object sender, RoutedEventArgs e)
        {
            var overlay = new PointPickerOverlay();
            overlay.ShowDialog();
            if (overlay.IsConfirmed)
            {
                var pt = new ClickPoint
                {
                    Index = _multiPoints.Count + 1,
                    X = overlay.SelectedX,
                    Y = overlay.SelectedY,
                    DelayMs = 500,
                    MouseButton = MouseButtonType.Left,
                    ClickAction = ClickActionType.Single
                };
                _multiPoints.Add(pt);
                UpdateMultiStatus();
            }
        }

        private void BtnDeleteMultiPoint_Click(object sender, RoutedEventArgs e)
        {
            if (DgMultiPoints.SelectedItem is ClickPoint selected)
            {
                _multiPoints.Remove(selected);
                for (int i = 0; i < _multiPoints.Count; i++)
                {
                    _multiPoints[i].Index = i + 1;
                }
                DgMultiPoints.Items.Refresh();
                UpdateMultiStatus();
            }
        }

        private void BtnClearMultiPoints_Click(object sender, RoutedEventArgs e)
        {
            _multiPoints.Clear();
            UpdateMultiStatus();
        }

        private void UpdateMultiStatus()
        {
            TxtMultiStatus.Text = _multiPoints.Count switch
            {
                0 => "Nenhum ponto cadastrado.",
                1 => "1 ponto cadastrado.",
                _ => $"{_multiPoints.Count} pontos cadastrados."
            };
        }

        private void BtnPauseMulti_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Pausar sequência")) return;
            _multiClickService.TogglePause();
        }

        private void BtnToggleMulti_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Iniciar/Parar sequência")) return;
            if (_multiClickService.IsRunning)
            {
                _appManager.StopMultiClick();
            }
            else
            {
                StartMultiClick();
            }
        }

        public void StartMultiClick()
        {
            if (_multiPoints.Count == 0)
            {
                TxtMultiStatus.Text = "Adicione pelo menos um ponto antes de iniciar.";
                return;
            }

            DgMultiPoints.CommitEdit(DataGridEditingUnit.Row, true);
            int.TryParse(TxtMultiLoops.Text, out int loops);
            _multiClickService.Start(_multiPoints.ToList(), Math.Max(0, loops));
        }

        #endregion

        #region Macro Recorder & Playback

        private void BtnRecordMacro_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Gravar macro")) return;
            StartMacroRecording();
        }

        public void StartMacroRecording()
        {
            if (_macroService.IsRecording || _macroService.IsPlaying) return;

            _macroActions.Clear();
            _macroService.RecordMouseMovement = ChkMacroRecordMoves.IsChecked == true;
            _macroService.RecordKeyboard = ChkMacroRecordKeyboard.IsChecked == true;
            _macroService.StartRecording();
        }

        private void BtnStopRecordMacro_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Parar macro")) return;
            if (_macroService.IsRecording) _macroService.StopRecording();
            else _macroService.StopPlayback();
        }

        private void BtnPauseMacro_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Pausar macro")) return;
            _macroService.TogglePlaybackPause();
        }

        private void BtnPlayMacro_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Reproduzir macro")) return;
            StartMacroPlayback();
        }

        public void StartMacroPlayback()
        {
            if (_macroService.IsPlaying || _macroService.IsRecording) return;

            if (_macroActions.Count == 0)
            {
                SetMacroStatus("Grave ou abra uma macro antes de reproduzir.", "WarningBrush");
                SetGlobalStatus("Nenhuma macro gravada", "WarningBrush");
                SoundService.PlayStopSound();
                return;
            }

            double speed = 1.0;
            if (CmbMacroSpeed.SelectedItem is ComboBoxItem speedItem && speedItem.Tag is string speedTag && double.TryParse(speedTag, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sp))
            {
                speed = sp;
            }

            int loops = 1;
            if (CmbMacroLoops.SelectedItem is ComboBoxItem loopItem && loopItem.Tag is string loopTag && int.TryParse(loopTag, out int lp))
            {
                loops = lp;
            }

            _macroService.RecordedActions = _macroActions.ToList();
            _macroService.StartPlayback(speed, loops);
        }

        private void BtnClearMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_macroService.IsRecording || _macroService.IsPlaying) return;
            _macroActions.Clear();
            _macroService.RecordedActions = new List<MacroAction>();
            UpdateMacroSummary();
            SetMacroStatus("Pronto", "TextMutedBrush");
        }

        private void CmbMacroSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void BtnSaveMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_macroActions.Count == 0)
            {
                SetMacroStatus("Não há ações para salvar.", "WarningBrush");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Macro do Gabriel Autoclick (*.json)|*.json",
                InitialDirectory = Constants.MacrosFolder,
                FileName = $"macro_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                _macroService.RecordedActions = _macroActions.ToList();
                _macroService.SaveMacroToFile(dlg.FileName);
                SetMacroStatus($"Salva em {Path.GetFileName(dlg.FileName)}", "TextMutedBrush");
            }
        }

        private void BtnLoadMacro_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Macro do Gabriel Autoclick (*.json)|*.json",
                InitialDirectory = Constants.MacrosFolder
            };

            if (dlg.ShowDialog() == true)
            {
                if (_macroService.LoadMacroFromFile(dlg.FileName))
                {
                    _macroActions.Clear();
                    foreach (var act in _macroService.RecordedActions)
                    {
                        _macroActions.Add(act);
                    }
                    UpdateMacroSummary();
                    SetMacroStatus($"Aberta: {Path.GetFileName(dlg.FileName)}", "TextMutedBrush");
                }
                else
                {
                    SetMacroStatus("Não foi possível abrir esse arquivo.", "WarningBrush");
                }
            }
        }

        #endregion

        #region Safe Zones & Window Restriction

        private void BtnAddSafeZone_Click(object sender, RoutedEventArgs e)
        {
            var overlay = new SafeZonePickerOverlay();
            overlay.ShowDialog();
            if (overlay.IsConfirmed)
            {
                var zone = new SafeZone
                {
                    Name = $"Zona {_safeZones.Count + 1}",
                    X = overlay.ZoneX,
                    Y = overlay.ZoneY,
                    Width = overlay.ZoneWidth,
                    Height = overlay.ZoneHeight,
                    IsEnabled = true,
                    Action = SafeZoneAction.BlockClick
                };

                _safeZones.Add(zone);
                _safeZoneService.AddSafeZone(zone);
                _settingsService.CurrentSettings.SafeZones = _safeZones.ToList();
                _settingsService.SaveSettings(_settingsService.CurrentSettings);
            }
        }

        private void BtnDeleteSafeZone_Click(object sender, RoutedEventArgs e)
        {
            if (DgSafeZones.SelectedItem is SafeZone selected)
            {
                _safeZones.Remove(selected);
                _safeZoneService.RemoveSafeZone(selected.Id);
                _settingsService.CurrentSettings.SafeZones = _safeZones.ToList();
                _settingsService.SaveSettings(_settingsService.CurrentSettings);
            }
        }

        private void ChkRestrictWindow_Changed(object sender, RoutedEventArgs e)
        {
            if (_windowRestrictionService.TargetWindow != null)
            {
                _windowRestrictionService.TargetWindow.RestrictToWindow = ChkRestrictWindow.IsChecked == true;
            }
        }

        private void CmbTargetWindows_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTargetWindows.SelectedItem is WindowTarget target)
            {
                target.RestrictToWindow = ChkRestrictWindow.IsChecked == true;
                _windowRestrictionService.TargetWindow = target;
            }
        }

        private void BtnRefreshWindows_Click(object sender, RoutedEventArgs e)
        {
            RefreshOpenWindowsList();
        }

        private void RefreshOpenWindowsList()
        {
            var list = WindowRestrictionService.GetOpenWindows();
            CmbTargetWindows.ItemsSource = list;
            if (list.Count > 0)
            {
                CmbTargetWindows.SelectedIndex = 0;
            }
        }

        #endregion

        #region Profiles Management

        private void LoadProfileListToUI()
        {
            LstProfiles.ItemsSource = null;
            LstProfiles.ItemsSource = _profileService.Profiles;
            if (_profileService.ActiveProfile != null)
            {
                LstProfiles.SelectedItem = _profileService.ActiveProfile;
                TxtActiveProfileName.Text = _profileService.ActiveProfile.Name;
            }
        }

        private void LstProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstProfiles.SelectedItem is Profile profile)
            {
                _profileService.SetActiveProfile(profile);
                TxtActiveProfileName.Text = profile.Name;
                ApplySettingsToUI(profile.Settings);

                _multiPoints.Clear();
                foreach (var pt in profile.MultiPoints)
                {
                    _multiPoints.Add(pt);
                }

                _macroActions.Clear();
                foreach (var act in profile.MacroSequence)
                {
                    _macroActions.Add(act);
                }
                MacroService.Renumber(_macroActions);
                UpdateMacroSummary();
                UpdateMultiStatus();
            }
        }

        private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
        {
            string name = $"Perfil {_profileService.Profiles.Count + 1}";
            var newProfile = _profileService.CreateProfile(name, "Perfil personalizado");
            LoadProfileListToUI();
            LstProfiles.SelectedItem = newProfile;
        }

        private void BtnDuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (LstProfiles.SelectedItem is Profile active)
            {
                var dup = _profileService.DuplicateProfile(active, $"{active.Name} (Cópia)");
                LoadProfileListToUI();
                LstProfiles.SelectedItem = dup;
            }
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (LstProfiles.SelectedItem is Profile active)
            {
                if (_profileService.DeleteProfile(active))
                {
                    LoadProfileListToUI();
                }
                else
                {
                    MessageBox.Show("Não é possível excluir o único perfil restante.", "Gabriel Autoclick", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnApplyAndSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profileService.ActiveProfile != null)
            {
                _profileService.ActiveProfile.Settings = GetActiveSettings();
                _profileService.ActiveProfile.MultiPoints = _multiPoints.ToList();
                _profileService.ActiveProfile.MacroSequence = _macroActions.ToList();
                _profileService.SaveProfiles();
                MessageBox.Show(this, $"Configurações salvas no perfil \"{_profileService.ActiveProfile.Name}\".", "Gabriel Autoclick", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profileService.ActiveProfile != null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "Gabriel Profile (*.json)|*.json",
                    FileName = $"{_profileService.ActiveProfile.Name.Replace(" ", "_")}.json"
                };

                if (dlg.ShowDialog() == true)
                {
                    string json = _profileService.ExportProfileJson(_profileService.ActiveProfile);
                    File.WriteAllText(dlg.FileName, json);
                    MessageBox.Show("Perfil exportado com sucesso!", "Gabriel Autoclick", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Gabriel Profile (*.json)|*.json"
            };

            if (dlg.ShowDialog() == true)
            {
                string json = File.ReadAllText(dlg.FileName);
                var imported = _profileService.ImportProfileJson(json);
                if (imported != null)
                {
                    LoadProfileListToUI();
                    LstProfiles.SelectedItem = imported;
                    MessageBox.Show($"Perfil '{imported.Name}' importado com sucesso!", "Gabriel Autoclick", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        #endregion

        #region Statistics

        private void UpdateStatisticsUI(AppStatistics stats)
        {
            TxtStatsSessionClicks.Text = stats.SessionClicks.ToString("N0");
            TxtStatsTotalClicks.Text = stats.TotalClicksLifetime.ToString("N0");
            TxtStatsTotalTime.Text = TimeSpan.FromSeconds(stats.TotalActiveSecondsLifetime).ToString(@"hh\:mm\:ss");
            TxtStatsAvgCps.Text = stats.SessionCurrentCps.ToString("0.0");
            TxtStatsPeakCps.Text = stats.PeakCpsLifetime.ToString("0.0");
            TxtStatsMacros.Text = stats.MacrosExecutedLifetime.ToString("N0");
        }

        private void BtnResetStats_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Zerar todas as estatísticas acumuladas? Essa ação não pode ser desfeita.", "Gabriel Autoclick", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _statisticsService.ResetStatistics();
            }
        }

        #endregion

        #region Settings & Appearance

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _settingsService == null) return;
            var cur = GetActiveSettingsForSaving();
            _settingsService.SaveSettings(cur);
            SoundService.StartSoundEnabled = cur.PlaySoundStart;
            SoundService.StopSoundEnabled = cur.PlaySoundStop;
        }

        private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || ChkAlwaysOnTop == null) return;
            this.Topmost = ChkAlwaysOnTop.IsChecked == true;
            Setting_Changed(sender, e);
        }

        private void ChkWallpaper_Changed(object sender, RoutedEventArgs e)
        {
            ApplyWallpaper();
            if (_isInitialized) Setting_Changed(sender, e);
        }

        private void CmbWallpaperIntensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyWallpaper();
            if (_isInitialized) Setting_Changed(sender, e);
        }

        private double SelectedWallpaperDarkness()
        {
            if (CmbWallpaperIntensity?.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
                double.TryParse(tag, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                return v;
            }
            return 0.80;
        }

        private void ApplyWallpaper()
        {
            if (ImgWallpaper == null || RectDarkOverlay == null || ChkWallpaper == null) return;
            bool show = ChkWallpaper.IsChecked == true;
            ImgWallpaper.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RectDarkOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RectDarkOverlay.Opacity = SelectedWallpaperDarkness();
            if (CmbWallpaperIntensity != null) CmbWallpaperIntensity.IsEnabled = show;
        }

        private void BtnPreviewStartSound_Click(object sender, RoutedEventArgs e)
        {
            bool prev = SoundService.StartSoundEnabled;
            SoundService.StartSoundEnabled = true;
            SoundService.PlayStartSound();
            SoundService.StartSoundEnabled = prev;
        }

        private void BtnPreviewStopSound_Click(object sender, RoutedEventArgs e)
        {
            bool prev = SoundService.StopSoundEnabled;
            SoundService.StopSoundEnabled = true;
            SoundService.PlayStopSound();
            SoundService.StopSoundEnabled = prev;
        }

        public AppSettings GetActiveSettingsForSaving()
        {
            var s = _settingsService.CurrentSettings;
            s.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            s.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            s.AlwaysOnTop = ChkAlwaysOnTop.IsChecked == true;
            s.AutoSaveSettings = ChkAutoSaveSettings.IsChecked == true;
            s.PlaySoundStart = ChkSoundStart.IsChecked == true;
            s.PlaySoundStop = ChkSoundStop.IsChecked == true;
            s.ShowNotifications = ChkShowNotifications.IsChecked == true;
            s.WallpaperEnabled = ChkWallpaper.IsChecked == true;
            s.WallpaperDarkness = SelectedWallpaperDarkness();
            s.SafeZones = _safeZones.ToList();
            s.BackgroundClick = BuildBgSettings();
            return s;
        }

        private void LoadSettingsToUI(AppSettings s)
        {
            ChkStartWithWindows.IsChecked = s.StartWithWindows;
            ChkMinimizeToTray.IsChecked = s.MinimizeToTray;
            ChkAlwaysOnTop.IsChecked = s.AlwaysOnTop;
            this.Topmost = s.AlwaysOnTop;

            ChkAutoSaveSettings.IsChecked = s.AutoSaveSettings;
            ChkSoundStart.IsChecked = s.PlaySoundStart;
            ChkSoundStop.IsChecked = s.PlaySoundStop;
            ChkShowNotifications.IsChecked = s.ShowNotifications;

            // Background photo: pick the intensity option closest to the saved value
            ChkWallpaper.IsChecked = s.WallpaperEnabled;
            int best = 1;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < CmbWallpaperIntensity.Items.Count; i++)
            {
                if (CmbWallpaperIntensity.Items[i] is ComboBoxItem it && it.Tag is string t &&
                    double.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) &&
                    Math.Abs(v - s.WallpaperDarkness) < bestDiff)
                {
                    bestDiff = Math.Abs(v - s.WallpaperDarkness);
                    best = i;
                }
            }
            CmbWallpaperIntensity.SelectedIndex = best;
            ApplyWallpaper();


            UpdateStatisticsUI(_statisticsService.Stats);
            UpdateHotkeysUI();
        }

        private void ApplySettingsToUI(ClickSettings s)
        {
            _isUpdatingInputs = true;

            TxtHours.Text = s.Hours.ToString();
            TxtMinutes.Text = s.Minutes.ToString();
            TxtSeconds.Text = s.Seconds.ToString();
            TxtMilliseconds.Text = s.Milliseconds.ToString();
            TxtDirectCps.Text = s.ComputedCps.ToString("0.0");
            TxtCardCps.Text = s.ComputedCps.ToString("0.0");
            if (TxtDashCpsInput != null) TxtDashCpsInput.Text = s.ComputedCps.ToString("0.0");
            if (TxtDashIntervalPreview != null) TxtDashIntervalPreview.Text = $"Intervalo de {s.TotalIntervalMs} ms";

            // Mouse button
            RbBtnLeft.IsChecked = s.MouseButton == MouseButtonType.Left;
            RbBtnRight.IsChecked = s.MouseButton == MouseButtonType.Right;
            RbBtnMiddle.IsChecked = s.MouseButton == MouseButtonType.Middle;

            // Action
            RbActionSingle.IsChecked = s.ClickAction == ClickActionType.Single;
            RbActionDouble.IsChecked = s.ClickAction == ClickActionType.Double;
            RbActionHoldDown.IsChecked = s.ClickAction == ClickActionType.HoldDown;
            RbActionHoldUp.IsChecked = s.ClickAction == ClickActionType.HoldUp;

            // Position
            RbPosFollow.IsChecked = s.PositionMode == ClickPositionMode.FollowCursor;
            RbPosCurrent.IsChecked = s.PositionMode == ClickPositionMode.CurrentPosition;
            RbPosFixed.IsChecked = s.PositionMode == ClickPositionMode.FixedPosition;
            TxtFixedX.Text = s.FixedX.ToString();
            TxtFixedY.Text = s.FixedY.ToString();

            // Repetition
            RbRepeatInfinite.IsChecked = s.RepeatMode == RepeatMode.UntilStopped;
            RbRepeatCount.IsChecked = s.RepeatMode == RepeatMode.Count;
            RbRepeatDuration.IsChecked = s.RepeatMode == RepeatMode.Duration;
            TxtRepeatCount.Text = s.RepeatCount.ToString();
            TxtRepeatDuration.Text = s.RepeatDurationSeconds.ToString();

            // Burst
            ChkBurstMode.IsChecked = s.BurstModeEnabled;
            TxtBurstCount.Text = s.BurstClicksCount.ToString();
            TxtBurstInterval.Text = s.BurstIntervalMs.ToString();
            TxtBurstCooldown.Text = s.BurstCooldownMs.ToString();

            // Random
            ChkRandomInterval.IsChecked = s.RandomIntervalEnabled;
            TxtMinRandomMs.Text = s.MinRandomIntervalMs.ToString();
            TxtMaxRandomMs.Text = s.MaxRandomIntervalMs.ToString();
            ChkRandomPos.IsChecked = s.RandomPositionEnabled;
            TxtRandomRadius.Text = s.RandomRadiusPixels.ToString();

            _isUpdatingInputs = false;
        }

        #endregion

        #region Hotkeys Management & In-App Modals

        private string _capturingActionTag = string.Empty;

        public void UpdateHotkeysUI()
        {
            if (TxtKeyToggleAutoclick == null || _hotkeyService?.Config == null) return;

            TxtKeyToggleAutoclick.Text = _hotkeyService.Config.ToggleAutoclick.ToDisplayString();
            TxtKeySelectPosition.Text = _hotkeyService.Config.SelectPosition.ToDisplayString();
            TxtKeyStartMacro.Text = _hotkeyService.Config.StartMacro.ToDisplayString();
            TxtKeyStopMacro.Text = _hotkeyService.Config.StopMacro.ToDisplayString();
            TxtKeyBgToggle.Text = _hotkeyService.Config.BackgroundToggle.ToDisplayString();
            TxtKeyBgPause.Text = _hotkeyService.Config.BackgroundPause.ToDisplayString();

            if (TxtDashShortcutHint != null)
            {
                TxtDashShortcutHint.Text = $"{_hotkeyService.Config.ToggleAutoclick.ToDisplayString()} inicia ou para  ·  F12 interrompe tudo";
            }
            if (TxtMacroHint != null)
            {
                TxtMacroHint.Text = $"{_hotkeyService.Config.StartMacro.ToDisplayString()} reproduz  ·  {_hotkeyService.Config.StopMacro.ToDisplayString()} para";
            }
        }

        private void BtnChangeHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _capturingActionTag = tag;
                string actionName = tag switch
                {
                    "ToggleAutoclick" => "Iniciar / parar autoclick",
                    "SelectPosition" => "Selecionar posição",
                    "StartMacro" => "Reproduzir macro",
                    "StopMacro" => "Parar macro",
                    "BackgroundToggle" => "Iniciar / parar clique em segundo plano",
                    "BackgroundPause" => "Pausar / continuar clique em segundo plano",
                    _ => tag
                };

                string currentKey = tag switch
                {
                    "ToggleAutoclick" => _hotkeyService.Config.ToggleAutoclick.ToDisplayString(),
                    "SelectPosition" => _hotkeyService.Config.SelectPosition.ToDisplayString(),
                    "StartMacro" => _hotkeyService.Config.StartMacro.ToDisplayString(),
                    "StopMacro" => _hotkeyService.Config.StopMacro.ToDisplayString(),
                    "BackgroundToggle" => _hotkeyService.Config.BackgroundToggle.ToDisplayString(),
                    "BackgroundPause" => _hotkeyService.Config.BackgroundPause.ToDisplayString(),
                    _ => "Nenhum"
                };

                TxtCaptureActionName.Text = actionName;
                TxtCaptureCurrentHotkey.Text = $"Atual: {currentKey}";

                ModalConflict.Visibility = Visibility.Collapsed;
                ModalRestoreConfirm.Visibility = Visibility.Collapsed;
                ModalCaptureHotkey.Visibility = Visibility.Visible;
                ModalHost.Visibility = Visibility.Visible;

                _hotkeyService.IsCapturingHotkey = true;
                LoggerService.Log($"[HOTKEY] Modale de captura aberto para ação: {tag}");
            }
        }

        private void OnHotkeyCapturedFromService(Key key, ModifierKeys mods)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(_capturingActionTag)) return;

                // 1. Validate Conflict or Locked Key
                if (_hotkeyService.CheckConflict(key, mods, _capturingActionTag, out string conflictName))
                {
                    ModalCaptureHotkey.Visibility = Visibility.Collapsed;
                    _hotkeyService.IsCapturingHotkey = false;

                    var tempItem = new HotkeyItem { Key = key, Modifiers = mods };
                    TxtConflictMessage.Text = $"\"{tempItem.ToDisplayString()}\" já é usado por \"{conflictName}\". Escolha outra tecla.";
                    ModalConflict.Visibility = Visibility.Visible;
                    LoggerService.Log($"[HOTKEY] Conflito detectado: {tempItem.ToDisplayString()} já usado por {conflictName}");
                    return;
                }

                // 2. Apply new key configuration
                switch (_capturingActionTag)
                {
                    case "ToggleAutoclick":
                        _hotkeyService.Config.ToggleAutoclick.Key = key;
                        _hotkeyService.Config.ToggleAutoclick.Modifiers = mods;
                        break;
                    case "SelectPosition":
                        _hotkeyService.Config.SelectPosition.Key = key;
                        _hotkeyService.Config.SelectPosition.Modifiers = mods;
                        break;
                    case "StartMacro":
                        _hotkeyService.Config.StartMacro.Key = key;
                        _hotkeyService.Config.StartMacro.Modifiers = mods;
                        break;
                    case "BackgroundToggle":
                        _hotkeyService.Config.BackgroundToggle.Key = key;
                        _hotkeyService.Config.BackgroundToggle.Modifiers = mods;
                        break;
                    case "BackgroundPause":
                        _hotkeyService.Config.BackgroundPause.Key = key;
                        _hotkeyService.Config.BackgroundPause.Modifiers = mods;
                        break;
                    case "StopMacro":
                        _hotkeyService.Config.StopMacro.Key = key;
                        _hotkeyService.Config.StopMacro.Modifiers = mods;
                        break;
                }

                // 3. Persist in settings & update dynamic service
                _hotkeyService.UpdateConfig(_hotkeyService.Config);
                _settingsService.CurrentSettings.Hotkeys = _hotkeyService.Config;
                _settingsService.SaveSettings(_settingsService.CurrentSettings);

                // 4. Update UI labels and hint
                UpdateHotkeysUI();

                // 5. Close capture modal
                _hotkeyService.IsCapturingHotkey = false;
                ModalCaptureHotkey.Visibility = Visibility.Collapsed;
                ModalHost.Visibility = Visibility.Collapsed;
                _capturingActionTag = string.Empty;
            });
        }

        private void BtnCancelCapture_Click(object sender, RoutedEventArgs e)
        {
            _hotkeyService.IsCapturingHotkey = false;
            ModalCaptureHotkey.Visibility = Visibility.Collapsed;
            ModalHost.Visibility = Visibility.Collapsed;
            _capturingActionTag = string.Empty;
        }

        private void BtnCloseConflictModal_Click(object sender, RoutedEventArgs e)
        {
            ModalConflict.Visibility = Visibility.Collapsed;
            ModalHost.Visibility = Visibility.Collapsed;
            _capturingActionTag = string.Empty;
        }

        private void BtnRestoreDefaultHotkeys_Click(object sender, RoutedEventArgs e)
        {
            ModalCaptureHotkey.Visibility = Visibility.Collapsed;
            ModalConflict.Visibility = Visibility.Collapsed;
            ModalRestoreConfirm.Visibility = Visibility.Visible;
            ModalHost.Visibility = Visibility.Visible;
        }

        private void BtnCancelRestoreHotkeys_Click(object sender, RoutedEventArgs e)
        {
            ModalRestoreConfirm.Visibility = Visibility.Collapsed;
            ModalHost.Visibility = Visibility.Collapsed;
        }

        private void BtnConfirmRestoreHotkeys_Click(object sender, RoutedEventArgs e)
        {
            _hotkeyService.Config.RestoreDefaults();
            _hotkeyService.UpdateConfig(_hotkeyService.Config);
            _settingsService.CurrentSettings.Hotkeys = _hotkeyService.Config;
            _settingsService.SaveSettings(_settingsService.CurrentSettings);

            UpdateHotkeysUI();

            ModalRestoreConfirm.Visibility = Visibility.Collapsed;
            ModalHost.Visibility = Visibility.Collapsed;
            LoggerService.Log("[HOTKEY] Atalhos restaurados para o padrão de fábrica.");
        }

        #endregion
    }
}
