using System;

namespace GabrielAutoclick.Models
{
    public enum SafeZoneAction
    {
        BlockClick = 0,
        PauseAutoclick = 1
    }

    public class SafeZone
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Zona Protegida";
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Width { get; set; } = 100;
        public int Height { get; set; } = 100;
        public bool IsEnabled { get; set; } = true;
        public SafeZoneAction Action { get; set; } = SafeZoneAction.BlockClick;

        public bool Contains(int px, int py)
        {
            if (!IsEnabled) return false;
            return px >= X && px <= (X + Width) && py >= Y && py <= (Y + Height);
        }
    }
}
