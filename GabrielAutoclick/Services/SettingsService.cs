using System;
using System.IO;
using System.Text.Json;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;
using Microsoft.Win32;

namespace GabrielAutoclick.Services
{
    public class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public AppSettings CurrentSettings { get; private set; }

        public event Action<AppSettings>? SettingsChanged;

        public SettingsService()
        {
            CurrentSettings = LoadSettings();
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (!Directory.Exists(Constants.AppDataFolder))
                {
                    Directory.CreateDirectory(Constants.AppDataFolder);
                }

                if (!Directory.Exists(Constants.MacrosFolder))
                {
                    Directory.CreateDirectory(Constants.MacrosFolder);
                }

                if (File.Exists(Constants.SettingsFilePath))
                {
                    string json = File.ReadAllText(Constants.SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            CurrentSettings = new AppSettings();
            SaveSettings(CurrentSettings);
            return CurrentSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(Constants.AppDataFolder))
                {
                    Directory.CreateDirectory(Constants.AppDataFolder);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(Constants.SettingsFilePath, json);
                CurrentSettings = settings;

                // Sync Windows Startup Registry
                UpdateWindowsStartup(settings.StartWithWindows);

                SettingsChanged?.Invoke(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private void UpdateWindowsStartup(bool enable)
        {
            try
            {
                const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
                if (key == null) return;

                string? appPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(appPath)) return;

                if (enable)
                {
                    key.SetValue("GabrielAutoclick", $"\"{appPath}\"");
                }
                else
                {
                    if (key.GetValue("GabrielAutoclick") != null)
                    {
                        key.DeleteValue("GabrielAutoclick", false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup registry error: {ex.Message}");
            }
        }
    }
}
