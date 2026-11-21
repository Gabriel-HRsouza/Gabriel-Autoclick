using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GabrielAutoclick.Helpers;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace GabrielAutoclick.Views
{
    /// <summary>
    /// Briefly outlines a target window and marks the click point, without taking focus
    /// or intercepting the mouse (transparent areas are click-through).
    /// </summary>
    public class TargetHighlightWindow : Window
    {
        private TargetHighlightWindow(Rect bounds, Point marker)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = Math.Max(10, bounds.Width);
            Height = Math.Max(10, bounds.Height);

            var accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
            var canvas = new Canvas();
            canvas.Children.Add(new Rectangle
            {
                Width = Width,
                Height = Height,
                Stroke = accent,
                StrokeThickness = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x18, 0x4C, 0x8D, 0xFF))
            });

            // Crosshair at the click point
            double mx = marker.X - bounds.Left, my = marker.Y - bounds.Top;
            var ring = new Ellipse { Width = 26, Height = 26, Stroke = Brushes.White, StrokeThickness = 2, Fill = accent };
            Canvas.SetLeft(ring, mx - 13);
            Canvas.SetTop(ring, my - 13);
            canvas.Children.Add(ring);
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White };
            Canvas.SetLeft(dot, mx - 3);
            Canvas.SetTop(dot, my - 3);
            canvas.Children.Add(dot);

            Content = canvas;
            Opacity = 0;
        }

        /// <summary>Shows the highlight for ~1.6 s. Coordinates are physical pixels; <paramref name="dpiScale"/> converts to WPF units.</summary>
        public static bool Flash(IntPtr hwnd, int clientX, int clientY, double dpiScale)
        {
            if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;

            var pt = new NativeMethods.POINT(clientX, clientY);
            NativeMethods.ClientToScreen(hwnd, ref pt);

            double s = dpiScale <= 0 ? 1 : dpiScale;
            var bounds = new Rect(r.Left / s, r.Top / s, (r.Right - r.Left) / s, (r.Bottom - r.Top) / s);
            var win = new TargetHighlightWindow(bounds, new Point(pt.X / s, pt.Y / s));
            win.Show();

            int ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (_, _) =>
            {
                ticks++;
                // fade in, hold, fade out (~1.6 s total)
                win.Opacity = ticks < 6 ? ticks / 6.0 : ticks > 34 ? Math.Max(0, (40 - ticks) / 6.0) : 1;
                if (ticks >= 40)
                {
                    timer.Stop();
                    win.Close();
                }
            };
            timer.Start();
            return true;
        }
    }
}
