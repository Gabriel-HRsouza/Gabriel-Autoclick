using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class MouseInputService
    {
        private static readonly Random Rng = new Random();

        public MouseInputService()
        {
            // Set Windows multimedia timer resolution to 1ms
            try
            {
                NativeMethods.TimeBeginPeriod(1);
            }
            catch { }
        }

        ~MouseInputService()
        {
            try
            {
                NativeMethods.TimeEndPeriod(1);
            }
            catch { }
        }

        public (int X, int Y) GetCursorPosition()
        {
            if (NativeMethods.GetCursorPos(out var pt))
            {
                return (pt.X, pt.Y);
            }
            return (0, 0);
        }

        public void SetCursorPosition(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
        }

        public uint SendMouseDown(MouseButtonType button)
        {
            uint downFlag = button switch
            {
                MouseButtonType.Left => NativeMethods.MOUSEEVENTF_LEFTDOWN,
                MouseButtonType.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
                MouseButtonType.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
                _ => NativeMethods.MOUSEEVENTF_LEFTDOWN
            };

            return SendMouseEvent(downFlag);
        }

        public uint SendMouseUp(MouseButtonType button)
        {
            uint upFlag = button switch
            {
                MouseButtonType.Left => NativeMethods.MOUSEEVENTF_LEFTUP,
                MouseButtonType.Right => NativeMethods.MOUSEEVENTF_RIGHTUP,
                MouseButtonType.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP,
                _ => NativeMethods.MOUSEEVENTF_LEFTUP
            };

            return SendMouseEvent(upFlag);
        }

        public bool SendClick(MouseButtonType button, int holdDurationMs = 8)
        {
            uint resDown = SendMouseDown(button);
            if (holdDurationMs > 0)
            {
                PreciseWaitMilliseconds(holdDurationMs);
            }
            uint resUp = SendMouseUp(button);

            return resDown > 0 && resUp > 0;
        }

        public bool SendDoubleClick(MouseButtonType button)
        {
            bool c1 = SendClick(button, 5);
            PreciseWaitMilliseconds(35);
            bool c2 = SendClick(button, 5);
            return c1 && c2;
        }

        public bool ExecuteClickAction(MouseButtonType button, ClickActionType action)
        {
            switch (action)
            {
                case ClickActionType.Single:
                    return SendClick(button, 8);
                case ClickActionType.Double:
                    return SendDoubleClick(button);
                case ClickActionType.HoldDown:
                    return SendMouseDown(button) > 0;
                case ClickActionType.HoldUp:
                    return SendMouseUp(button) > 0;
                default:
                    return SendClick(button, 8);
            }
        }

        public (int X, int Y) ApplyRandomRadius(int baseX, int baseY, int radius)
        {
            if (radius <= 0) return (baseX, baseY);
            int dx = Rng.Next(-radius, radius + 1);
            int dy = Rng.Next(-radius, radius + 1);
            return (baseX + dx, baseY + dy);
        }

        public uint SendMouseEvent(uint flags)
        {
            uint result = 0;
            try
            {
                var input = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_MOUSE,
                    u = new NativeMethods.INPUTUNION
                    {
                        mi = new NativeMethods.MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = flags,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[] { input };
                int size = Marshal.SizeOf(typeof(NativeMethods.INPUT));
                result = NativeMethods.SendInput(1, inputs, size);
            }
            catch { }

            // Reliable fallback to mouse_event if SendInput is blocked by OS session/game restrictions
            if (result == 0)
            {
                try
                {
                    NativeMethods.mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
                    result = 1;
                }
                catch { }
            }

            return result;
        }

        public static void PreciseWaitMilliseconds(int milliseconds, CancellationToken cancellationToken = default)
        {
            if (milliseconds <= 0) return;
            if (milliseconds > 15)
            {
                // Sleep majority of the time to keep CPU usage minimal, spin-wait last few ms
                long targetTicks = Stopwatch.GetTimestamp() + (milliseconds * Stopwatch.Frequency / 1000);
                int sleepMs = milliseconds - 4;
                if (sleepMs > 0)
                {
                    if (cancellationToken.WaitHandle.WaitOne(sleepMs))
                        return;
                }

                while (Stopwatch.GetTimestamp() < targetTicks)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    Thread.SpinWait(10);
                }
            }
            else
            {
                long targetTicks = Stopwatch.GetTimestamp() + (milliseconds * Stopwatch.Frequency / 1000);
                while (Stopwatch.GetTimestamp() < targetTicks)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    Thread.SpinWait(10);
                }
            }
        }

        public static void PreciseWaitMicroseconds(long microseconds)
        {
            long targetTicks = Stopwatch.GetTimestamp() + (microseconds * Stopwatch.Frequency / 1000000);
            while (Stopwatch.GetTimestamp() < targetTicks)
            {
                Thread.SpinWait(5);
            }
        }
    }
}
