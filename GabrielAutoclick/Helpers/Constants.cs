using System;
using System.IO;

namespace GabrielAutoclick.Helpers
{
    public static class Constants
    {
        public const string AppName = "Gabriel Autoclick";
        public const string AppSubtitle = "Advanced Mouse Automation";
        public const string AppVersion = "1.0.0";

        public static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GabrielAutoclick"
        );

        public static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
        public static readonly string ProfilesFilePath = Path.Combine(AppDataFolder, "profiles.json");
        public static readonly string StatisticsFilePath = Path.Combine(AppDataFolder, "statistics.json");
        public static readonly string MacrosFolder = Path.Combine(AppDataFolder, "macros");

        public const int MinSafeIntervalMs = 1;
        public const int DefaultMinSafeIntervalMs = 10;
        public const int MaxCpsLimit = 1000;

        // Hotkey IDs
        public const int HOTKEY_ID_START_STOP = 9001;
        public const int HOTKEY_ID_PICK_POINT = 9002;
        public const int HOTKEY_ID_START_MACRO = 9003;
        public const int HOTKEY_ID_STOP_MACRO = 9004;
        public const int HOTKEY_ID_PANIC = 9005;
    }
}
