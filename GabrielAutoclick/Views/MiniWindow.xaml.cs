using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GabrielAutoclick.Models;
using GabrielAutoclick.Services;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace GabrielAutoclick.Views
{
    public partial class MiniWindow : Window
    {
        private readonly AutoClickService _autoClickService;
        private readonly MainWindow _mainWindow;

        public MiniWindow(AutoClickService autoClickService, MainWindow mainWindow)
        {
            InitializeComponent();
            _autoClickService = autoClickService;
            _mainWindow = mainWindow;

            _autoClickService.StateChanged += OnAutoClickStateChanged;
            _autoClickService.MetricsUpdated += OnMetricsUpdated;

            UpdateVisualState(_autoClickService.State);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    // Double click restores MainWindow
                    AppManager.Instance.ShowMainWindow();
                }
                else
                {
                    this.DragMove();
                }
            }
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (GabrielAutoclick.Helpers.SelfClickGuard.IsCurrentInputSynthetic()) return;
            AppManager.Instance.ToggleAutoClick(AutoClickStopReason.UserButton);
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            AppManager.Instance.ShowMainWindow();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            AppManager.Instance.ShowMainWindow();
        }

        private void OnAutoClickStateChanged(AutoClickState state)
        {
            Dispatcher.Invoke(() => UpdateVisualState(state));
        }

        private void OnMetricsUpdated(long clicks, double cps, TimeSpan elapsed)
        {
            Dispatcher.Invoke(() =>
            {
                TxtCps.Text = cps > 0 ? cps.ToString("0.0") : _mainWindow.GetActiveSettings().ComputedCps.ToString("0.0");
            });
        }

        private void UpdateVisualState(AutoClickState state)
        {
            (string text, string brushKey) = state switch
            {
                AutoClickState.Running => ("Clicando", "SuccessBrush"),
                AutoClickState.Paused => ("Pausado", "WarningBrush"),
                AutoClickState.CountingDown => ("Aguardando", "AccentBrush"),
                AutoClickState.Finished => ("Concluído", "AccentBrush"),
                _ => ("Desligado", "TextMutedBrush")
            };

            bool active = state is AutoClickState.Running or AutoClickState.Paused or AutoClickState.CountingDown;
            StatusDot.Fill = (Brush)FindResource(brushKey);
            TxtStatus.Text = text;
            TxtBtnIcon.Text = active ? "" : "";
            TxtBtnLabel.Text = active ? "Parar" : "Iniciar";
            BtnToggle.Style = (Style)FindResource(active ? "SolidDangerButton" : "PrimaryButton");
        }
    }
}
