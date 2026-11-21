using System;

namespace GabrielAutoclick.Models
{
    public class WindowTarget
    {
        public IntPtr Hwnd { get; set; } = IntPtr.Zero;
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public uint ProcessId { get; set; } = 0;
        public bool RestrictToWindow { get; set; } = false;

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                return ProcessName;
            }
            return $"{ProcessName} - {Title}";
        }
    }
}
