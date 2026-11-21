using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GabrielAutoclick.Models;
using GabrielAutoclick.Services;
using GabrielAutoclick.Views;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace GabrielAutoclick
{
    /// <summary>"Clique em segundo plano" page.</summary>
    public partial class MainWindow
    {
        private BackgroundClickService _bgService => _appManager.BackgroundClickService;
        private readonly ObservableCollection<BackgroundClickPoint> _bgPoints = new ObservableCollection<BackgroundClickPoint>();
        private DispatcherTimer? _bgWatchTimer;
        private bool _bgLoading;

        private static readonly string[] BgMethodHints =
        {
            "Recomendado. Envia o clique direto para o controle da janela. Não mexe no seu cursor e funciona com a janela atrás de outras. Compatível com a maioria dos programas comuns.",
            "Igual ao anterior, mas espera o programa processar cada clique. Útil quando o programa ignora cliques muito rápidos.",
            "Aciona o botão ou item sob o ponto pela interface de acessibilidade do Windows. Ótimo para botões e menus; não faz clique direito.",
            "Para jogos e programas que ignoram os outros métodos. Move o cursor até o ponto por cerca de 20 ms e devolve. A janela precisa estar visível nesse ponto."
        };

        private void InitBackgroundClick()
        {
            DgBgPoints.ItemsSource = _bgPoints;

            _bgLoading = true;
            var cfg = _settingsService.CurrentSettings.BackgroundClick ?? new BackgroundClickSettings();
            TxtBgInterval.Text = cfg.IntervalMs.ToString();
            TxtBgCycles.Text = cfg.Cycles.ToString();
            CmbBgMethod.SelectedIndex = (int)cfg.Method;
            CmbBgDefaultKind.SelectedIndex = (int)cfg.DefaultKind;
            ChkBgSendMove.IsChecked = cfg.SendMouseMove;

            foreach (var p in cfg.Points)
            {
                // Saved points have no live handle: look for the program's window again
                p.Status = BackgroundClickService.EnsureWindow(p, out _) ? "Pronto" : "Janela não encontrada";
                _bgPoints.Add(p);
            }
            RenumberBgPoints();
            _bgLoading = false;

            _bgService.StateChanged += s => Dispatcher.BeginInvoke(() => UpdateBgState(s));
            _bgService.StatusUpdated += msg => Dispatcher.BeginInvoke(() => TxtBgSubstatus.Text = msg);
            _bgService.ProgressUpdated += (clicks, cycles) => Dispatcher.BeginInvoke(() =>
            {
                TxtBgClicks.Text = clicks.ToString("N0");
                TxtBgCyclesDone.Text = cycles.ToString("N0");
            });
            _bgService.ErrorOccurred += msg => Dispatcher.BeginInvoke(() => OnBgError(msg));

            // Keeps the "Alvo selecionado" panel and point statuses in sync with the real windows
            _bgWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _bgWatchTimer.Tick += (_, _) => RefreshBgTargetPanel();
            _bgWatchTimer.Start();

            UpdateBgState(BackgroundRunState.Stopped);
            RefreshBgTargetPanel();
        }

        public BackgroundClickSettings BuildBgSettings()
        {
            int interval = int.TryParse(TxtBgInterval.Text, out int iv) ? iv : 1000;
            int.TryParse(TxtBgCycles.Text, out int cycles);
            return new BackgroundClickSettings
            {
                Points = _bgPoints.ToList(),
                IntervalMs = Math.Max(10, interval),
                Cycles = Math.Max(0, cycles),
                Method = (BackgroundClickMethod)Math.Max(0, CmbBgMethod.SelectedIndex),
                DefaultKind = (BackgroundClickKind)Math.Max(0, CmbBgDefaultKind.SelectedIndex),
                SendMouseMove = ChkBgSendMove.IsChecked == true
            };
        }

        private void SaveBgSettings()
        {
            if (_bgLoading || !_isInitialized) return;
            _settingsService.CurrentSettings.BackgroundClick = BuildBgSettings();
            _settingsService.SaveSettings(_settingsService.CurrentSettings);
        }

        #region Run control

        public void ToggleBackgroundClick()
        {
            if (_bgService.IsActive) _bgService.Stop();
            else StartBackgroundClick();
        }

        private void StartBackgroundClick()
        {
            DgBgPoints.CommitEdit(DataGridEditingUnit.Row, true);

            if (_bgPoints.Count == 0)
            {
                ShowBgBanner("Adicione pelo menos uma posição com \"Selecionar posição\" antes de iniciar.", isError: false);
                SoundService.PlayStopSound();
                return;
            }

            if (!int.TryParse(TxtBgInterval.Text, out int interval) || interval < 10)
            {
                ShowBgBanner("O intervalo entre ciclos precisa ser um número de pelo menos 10 ms.", isError: false);
                return;
            }

            HideBgBanner();
            var cfg = BuildBgSettings();
            SaveBgSettings();
            _bgService.Start(_bgPoints.ToList(), cfg);
        }

        private void BtnBgStart_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Iniciar segundo plano")) return;
            StartBackgroundClick();
        }

        private void BtnBgPause_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Pausar segundo plano")) return;
            _bgService.TogglePause();
        }

        private void BtnBgStop_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreSelfClick("Parar segundo plano")) return;
            _bgService.Stop();
        }

        private void UpdateBgState(BackgroundRunState state)
        {
            (string text, string brush) = state switch
            {
                BackgroundRunState.Running => ("Clicando em segundo plano", "SuccessBrush"),
                BackgroundRunState.Paused => ("Pausado", "WarningBrush"),
                BackgroundRunState.Finished => ("Concluído", "AccentBrush"),
                _ => ("Parado", "TextMutedBrush")
            };

            TxtBgState.Text = text;
            BgStatusDot.Fill = (Brush)FindResource(brush);

            bool active = state is BackgroundRunState.Running or BackgroundRunState.Paused;
            BtnBgStart.IsEnabled = !active;
            BtnBgPause.IsEnabled = active;
            BtnBgStop.IsEnabled = active;
            TxtBgPauseIcon.Text = state == BackgroundRunState.Paused ? "" : "";
            TxtBgPauseText.Text = state == BackgroundRunState.Paused ? "Continuar" : "Pausar";

            string hotkeys = $"{_hotkeyService.Config.BackgroundToggle.ToDisplayString()} inicia ou para  ·  {_hotkeyService.Config.BackgroundPause.ToDisplayString()} pausa";
            if (!active && state == BackgroundRunState.Stopped && (TxtBgSubstatus.Text == "Parado" || TxtBgSubstatus.Text.StartsWith("Selecione")))
            {
                TxtBgSubstatus.Text = _bgPoints.Count == 0 ? "Selecione uma posição para começar." : hotkeys;
            }

            _appManager.TrayIconService.UpdateTooltip(active ? $"Segundo plano: {text.ToLowerInvariant()}" : "Desligado");
            UpdateVisualStatus(_autoClickService.State);
        }

        private void OnBgError(string message)
        {
            ShowBgBanner($"A automação em segundo plano parou. {message}", isError: true);

            // The app may be minimized to the tray while running in the background
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                _appManager.TrayIconService.ShowNotification("Clique em segundo plano parou", message,
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
        }

        #endregion

        #region Points

        private async void BtnBgPick_Click(object sender, RoutedEventArgs e)
        {
            // Hide this window so it cannot be picked by mistake and the target is reachable
            Hide();
            await Task.Delay(250);

            var overlay = new PointPickerOverlay();
            overlay.ShowDialog();

            // Identify the target window while Gabriel Autoclick is still hidden; showing it first
            // made the point resolve to our own window ("Esse ponto é do próprio Gabriel Autoclick").
            BackgroundClickPoint? point = null;
            string error = string.Empty;
            if (overlay.IsConfirmed)
            {
                await Task.Delay(50); // let the overlay window be fully destroyed
                point = BackgroundClickService.CaptureAt(overlay.SelectedX, overlay.SelectedY, out error);
            }

            Show();
            Activate();

            if (!overlay.IsConfirmed) return;
            if (point == null)
            {
                ShowBgBanner(error, isError: true);
                return;
            }

            point.Kind = (BackgroundClickKind)Math.Max(0, CmbBgDefaultKind.SelectedIndex);
            point.Status = "Pronto";
            _bgPoints.Add(point);
            RenumberBgPoints();
            DgBgPoints.SelectedItem = point;
            SaveBgSettings();

            string? warning = BackgroundClickService.GetCompatibilityWarning(point);
            if (warning != null) ShowBgBanner(warning, isError: false);
            else HideBgBanner();

            TxtBgSubstatus.Text = $"Posição adicionada em \"{point.WindowDisplay}\". Use Testar para conferir.";
            TargetHighlightWindow.Flash(point.Hwnd, point.ClientX, point.ClientY, VisualTreeHelper.GetDpi(this).DpiScaleX);
        }

        private async void BtnBgTest_Click(object sender, RoutedEventArgs e)
        {
            if (DgBgPoints.SelectedItem is not BackgroundClickPoint p)
            {
                TxtBgSubstatus.Text = "Selecione um ponto na lista para testar.";
                return;
            }

            if (!BackgroundClickService.EnsureWindow(p, out string err))
            {
                p.Status = err;
                ShowBgBanner($"Não foi possível testar: {err}.", isError: true);
                return;
            }

            var method = (BackgroundClickMethod)Math.Max(0, CmbBgMethod.SelectedIndex);
            bool sendMove = ChkBgSendMove.IsChecked == true;
            BtnBgTest.IsEnabled = false;

            (bool ok, string error) = await Task.Run(() =>
            {
                bool r = BackgroundClickService.SendClick(p, method, sendMove, out string er);
                return (r, er);
            });

            BtnBgTest.IsEnabled = true;
            p.Status = ok ? "Teste enviado" : error;
            if (ok)
            {
                HideBgBanner();
                TxtBgSubstatus.Text = "Clique de teste enviado. Se o programa não reagiu, experimente outro método de envio.";
            }
            else
            {
                ShowBgBanner(error, isError: true);
            }
        }

        private void BtnBgRemove_Click(object sender, RoutedEventArgs e)
        {
            if (DgBgPoints.SelectedItem is BackgroundClickPoint p)
            {
                _bgPoints.Remove(p);
                RenumberBgPoints();
                SaveBgSettings();
            }
        }

        private void BtnBgClear_Click(object sender, RoutedEventArgs e)
        {
            if (_bgService.IsActive) _bgService.Stop();
            _bgPoints.Clear();
            SaveBgSettings();
            RefreshBgTargetPanel();
        }

        private void BtnBgHighlight_Click(object sender, RoutedEventArgs e)
        {
            if (DgBgPoints.SelectedItem is not BackgroundClickPoint p) return;
            BackgroundClickService.EnsureWindow(p, out _);
            if (!TargetHighlightWindow.Flash(p.Hwnd, p.ClientX, p.ClientY, VisualTreeHelper.GetDpi(this).DpiScaleX))
            {
                TxtBgSubstatus.Text = BackgroundClickService.GetCondition(p) == BackgroundClickService.WindowCondition.Minimized
                    ? "A janela está minimizada; restaure-a para vê-la destacada."
                    : "A janela não está aberta.";
            }
        }

        private void RenumberBgPoints()
        {
            for (int i = 0; i < _bgPoints.Count; i++) _bgPoints[i].Index = i + 1;
        }

        private void DgBgPoints_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBgTargetPanel();

        private void CmbBgPointKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_bgLoading || DgBgPoints?.SelectedItem is not BackgroundClickPoint p || CmbBgPointKind.SelectedIndex < 0) return;
            p.Kind = (BackgroundClickKind)CmbBgPointKind.SelectedIndex;
            SaveBgSettings();
        }

        private void CmbBgMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = Math.Max(0, CmbBgMethod.SelectedIndex);
            if (TxtBgMethodHint != null) TxtBgMethodHint.Text = BgMethodHints[Math.Min(i, BgMethodHints.Length - 1)];
            SaveBgSettings();
        }

        private void RefreshBgTargetPanel()
        {
            if (PanelBgTarget == null) return;

            if (DgBgPoints.SelectedItem is not BackgroundClickPoint p)
            {
                PanelBgTarget.Visibility = Visibility.Collapsed;
                TxtBgNoTarget.Visibility = Visibility.Visible;
                return;
            }

            PanelBgTarget.Visibility = Visibility.Visible;
            TxtBgNoTarget.Visibility = Visibility.Collapsed;

            var condition = BackgroundClickService.GetCondition(p);
            if (condition == BackgroundClickService.WindowCondition.Normal || condition == BackgroundClickService.WindowCondition.Minimized)
            {
                string title = BackgroundClickService.GetWindowTitle(p.Hwnd);
                if (!string.IsNullOrEmpty(title) && title != p.WindowTitle) p.WindowTitle = title;
            }

            TxtBgTargetTitle.Text = p.WindowDisplay;
            TxtBgTargetTitle.ToolTip = p.WindowDisplay;
            TxtBgTargetProcess.Text = $"{p.ProcessName}.exe";
            TxtBgTargetPos.Text = $"X {p.ClientX}, Y {p.ClientY} (dentro da janela)";
            TxtBgTargetHandle.Text = p.Hwnd == IntPtr.Zero ? "—" : $"0x{p.Hwnd.ToInt64():X}  ·  {p.ClassName}";
            TxtBgTargetHandle.ToolTip = TxtBgTargetHandle.Text;

            (string stateText, string brush) = condition switch
            {
                BackgroundClickService.WindowCondition.Minimized => ("Minimizada", "WarningBrush"),
                BackgroundClickService.WindowCondition.Closed => ("Fechada", "DangerBrush"),
                _ => ("Aberta", "SuccessBrush")
            };
            TxtBgTargetState.Text = stateText;
            BgTargetDot.Fill = (Brush)FindResource(brush);

            if (condition == BackgroundClickService.WindowCondition.Closed && !_bgService.IsActive && p.Status != "Janela fechada")
            {
                p.Status = "Janela fechada";
            }

            _bgLoading = true;
            CmbBgPointKind.SelectedIndex = (int)p.Kind;
            _bgLoading = false;
        }

        #endregion

        #region Banner

        private void ShowBgBanner(string text, bool isError)
        {
            TxtBgBanner.Text = text;
            var color = isError ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("WarningBrush");
            TxtBgBannerIcon.Foreground = color;
            TxtBgBannerIcon.Text = isError ? "" : "";
            BgBanner.Background = (Brush)new SolidColorBrush(isError ? Color.FromArgb(0x1F, 0xE5, 0x48, 0x4D) : Color.FromArgb(0x1F, 0xE0, 0xA4, 0x3B));
            BgBanner.BorderBrush = (Brush)new SolidColorBrush(isError ? Color.FromArgb(0x66, 0xE5, 0x48, 0x4D) : Color.FromArgb(0x66, 0xE0, 0xA4, 0x3B));
            BgBanner.Visibility = Visibility.Visible;
        }

        private void HideBgBanner() => BgBanner.Visibility = Visibility.Collapsed;

        private void BtnBgCloseBanner_Click(object sender, RoutedEventArgs e) => HideBgBanner();

        #endregion
    }
}
