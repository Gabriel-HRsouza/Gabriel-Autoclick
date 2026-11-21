using System;
using System.Collections.Generic;

namespace GabrielAutoclick.Models
{
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
        public bool AutoSaveSettings { get; set; } = true;
        public bool PlaySoundOnStart { get; set; } = true;
        public bool PlaySoundOnStop { get; set; } = true;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool PlaySoundStart
        {
            get => PlaySoundOnStart;
            set => PlaySoundOnStart = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool PlaySoundStop
        {
            get => PlaySoundOnStop;
            set => PlaySoundOnStop = value;
        }
        public bool ShowNotifications { get; set; } = true;

        public bool WallpaperEnabled { get; set; } = true;
        public double WallpaperDarkness { get; set; } = 0.78; // 0.0 transparent, 1.0 solid dark

        public int SafeIntervalMinMs { get; set; } = 10;

        // Auto pause configurations
        public bool AutoPauseOnWindowChange { get; set; } = false;
        public bool AutoPauseOnLock { get; set; } = true;
        public bool AutoPauseOnTargetClose { get; set; } = true;
        public bool StopOnSafeZone { get; set; } = false; // true = stop, false = block click

        public HotkeyConfig Hotkeys { get; set; } = new HotkeyConfig();
        public List<SafeZone> SafeZones { get; set; } = new List<SafeZone>();
        public BackgroundClickSettings BackgroundClick { get; set; } = new BackgroundClickSettings();

        public AppSettings()
        {
            // Default safe zones (e.g. top right close button region optional, or empty list)
        }
    }
}
