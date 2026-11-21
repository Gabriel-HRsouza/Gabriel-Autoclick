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

        public bool IsPointInSafeZone(int x, int y, out SafeZone? hitZone)
        {
            hitZone = null;
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
