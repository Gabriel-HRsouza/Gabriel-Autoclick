using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using GabrielAutoclick.Helpers;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GabrielAutoclick.Views
{
    public partial class SafeZonePickerOverlay : Window
    {
        private bool _isDragging = false;
        private Point _startPoint;
        private NativeMethods.POINT _startScreenPt;

        public int ZoneX { get; private set; }
        public int ZoneY { get; private set; }
        public int ZoneWidth { get; private set; }
        public int ZoneHeight { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public SafeZonePickerOverlay()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(OverlayCanvas);
                NativeMethods.GetCursorPos(out _startScreenPt);

                Canvas.SetLeft(SelectionRect, _startPoint.X);
                Canvas.SetTop(SelectionRect, _startPoint.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
                SelectionRect.Visibility = Visibility.Visible;

                InfoHud.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var curPoint = e.GetPosition(OverlayCanvas);
                double x = Math.Min(_startPoint.X, curPoint.X);
                double y = Math.Min(_startPoint.Y, curPoint.Y);
                double w = Math.Abs(curPoint.X - _startPoint.X);
                double h = Math.Abs(curPoint.Y - _startPoint.Y);

                Canvas.SetLeft(SelectionRect, x);
                Canvas.SetTop(SelectionRect, y);
                SelectionRect.Width = w;
                SelectionRect.Height = h;

                Canvas.SetLeft(InfoHud, curPoint.X + 15);
                Canvas.SetTop(InfoHud, curPoint.Y + 15);
                TxtInfo.Text = $"Dimensões: {(int)w} x {(int)h} px";
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isDragging)
            {
                _isDragging = false;
                NativeMethods.GetCursorPos(out var endScreenPt);

                int screenX = Math.Min(_startScreenPt.X, endScreenPt.X);
                int screenY = Math.Min(_startScreenPt.Y, endScreenPt.Y);
                int screenW = Math.Abs(endScreenPt.X - _startScreenPt.X);
                int screenH = Math.Abs(endScreenPt.Y - _startScreenPt.Y);

                if (screenW > 5 && screenH > 5)
                {
                    ZoneX = screenX;
                    ZoneY = screenY;
                    ZoneWidth = screenW;
                    ZoneHeight = screenH;
                    IsConfirmed = true;
                }

                Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsConfirmed = false;
                Close();
            }
        }
    }
}
