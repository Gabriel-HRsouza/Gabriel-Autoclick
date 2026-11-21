using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class WindowRestrictionService
    {
        public WindowTarget? TargetWindow { get; set; }

        public bool IsTargetWindowActive()
        {
            if (TargetWindow == null || !TargetWindow.RestrictToWindow)
            {
                return true; // No restriction
            }

            IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return false;

            if (TargetWindow.Hwnd != IntPtr.Zero && TargetWindow.Hwnd == fgHwnd)
            {
                return true;
            }

            // Also check by process ID or window title in case handle refreshed
            NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
            if (TargetWindow.ProcessId != 0 && TargetWindow.ProcessId == fgPid)
            {
                return true;
            }

            return false;
        }

        public static List<WindowTarget> GetOpenWindows()
        {
            var list = new List<WindowTarget>();

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                int length = NativeMethods.GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var builder = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, builder, builder.Capacity);
                string title = builder.ToString();

                if (string.IsNullOrWhiteSpace(title)) return true;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                string processName = "Desconhecido";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch { }

                // Skip common system / shell overlay windows
                if (processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(title))
                {
                    return true;
                }

                list.Add(new WindowTarget
                {
                    Hwnd = hWnd,
                    Title = title,
                    ProcessName = processName,
                    ProcessId = pid
                });

                return true;
            }, IntPtr.Zero);

            return list;
        }
    }
}
