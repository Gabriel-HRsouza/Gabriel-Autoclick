using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GabrielAutoclick.Helpers;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace GabrielAutoclick.Views
{
    public partial class PointPickerOverlay : Window
    {
        public int SelectedX { get; private set; }
        public int SelectedY { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public PointPickerOverlay()
        {
            InitializeComponent();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (NativeMethods.GetCursorPos(out var pt))
            {
                TxtX.Text = pt.X.ToString();
                TxtY.Text = pt.Y.ToString();

                // Position HUD slightly offset from cursor
                var mousePoint = e.GetPosition(this);
                double left = mousePoint.X + 20;
                double top = mousePoint.Y + 20;

                if (left + 150 > this.ActualWidth) left = mousePoint.X - 160;
                if (top + 60 > this.ActualHeight) top = mousePoint.Y - 60;

                Canvas.SetLeft(CoordHud, left);
                Canvas.SetTop(CoordHud, top);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (NativeMethods.GetCursorPos(out var pt))
                {
                    SelectedX = pt.X;
                    SelectedY = pt.Y;
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
