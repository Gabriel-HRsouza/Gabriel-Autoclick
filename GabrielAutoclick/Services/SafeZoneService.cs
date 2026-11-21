using System;
using System.Collections.Generic;
using System.Linq;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class SafeZoneService
    {
        public List<SafeZone> SafeZones { get; set; } = new List<SafeZone>();

        public event Action<SafeZone, int, int>? SafeZoneTriggered;

        public SafeZoneService(List<SafeZone>? initialZones = null)
        {
            if (initialZones != null)
            {
                SafeZones = initialZones;
            }
        }

        private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

        /// <summary>Zone reported when the point is over one of Gabriel Autoclick's own windows.</summary>
        public static readonly SafeZone OwnWindowZone = new SafeZone { Name = "Gabriel Autoclick", IsEnabled = true };

        /// <summary>
        /// True when the screen point is over any window of this app (main window, compact mode,
        /// dialogs). Automatic clicks there are always blocked so the app never clicks its own
        /// buttons, e.g. pressing "Stop" or changing settings by itself.
        /// </summary>
        public static bool IsOverOwnWindow(int x, int y)
        {
            IntPtr hwnd = Helpers.NativeMethods.WindowFromPoint(new Helpers.NativeMethods.POINT(x, y));
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = Helpers.NativeMethods.GetAncestor(hwnd, Helpers.NativeMethods.GA_ROOT);
            Helpers.NativeMethods.GetWindowThreadProcessId(root != IntPtr.Zero ? root : hwnd, out uint pid);
            return pid == OwnProcessId;
        }

        public bool IsPointInSafeZone(int x, int y, out SafeZone? hitZone)
        {
            hitZone = null;

            if (IsOverOwnWindow(x, y))
            {
                hitZone = OwnWindowZone;
                return true;
            }

            foreach (var zone in SafeZones.Where(z => z.IsEnabled))
            {
                if (zone.Contains(x, y))
                {
                    hitZone = zone;
                    SafeZoneTriggered?.Invoke(zone, x, y);
                    return true;
                }
            }
            return false;
        }

        public void AddSafeZone(SafeZone zone)
        {
            SafeZones.Add(zone);
        }

        public bool RemoveSafeZone(string id)
        {
            var zone = SafeZones.FirstOrDefault(z => z.Id == id);
            if (zone != null)
            {
                return SafeZones.Remove(zone);
            }
            return false;
        }
    }
}
