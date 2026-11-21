using System;
using System.IO;
using System.Text.Json;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class StatisticsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public AppStatistics Stats { get; private set; }

        public event Action<AppStatistics>? StatisticsUpdated;

        public StatisticsService()
        {
            Stats = LoadStatistics();
        }

        public AppStatistics LoadStatistics()
        {
            try
            {
                if (File.Exists(Constants.StatisticsFilePath))
                {
                    string json = File.ReadAllText(Constants.StatisticsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppStatistics>(json, JsonOptions);
                    if (loaded != null)
                    {
                        Stats = loaded;
                        return Stats;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load statistics: {ex.Message}");
            }

            Stats = new AppStatistics();
            SaveStatistics();
            return Stats;
        }

        public void SaveStatistics()
        {
            try
            {
                if (!Directory.Exists(Constants.AppDataFolder))
                {
                    Directory.CreateDirectory(Constants.AppDataFolder);
                }

                string json = JsonSerializer.Serialize(Stats, JsonOptions);
                File.WriteAllText(Constants.StatisticsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save statistics: {ex.Message}");
            }
        }

        public void RegisterClick()
        {
            Stats.SessionClicks++;
            Stats.TotalClicksLifetime++;
        }

        public void RegisterMacroExecution()
        {
            Stats.SessionMacrosExecuted++;
            Stats.MacrosExecutedLifetime++;
            SaveStatistics();
            StatisticsUpdated?.Invoke(Stats);
        }

        public void UpdateRuntimeStats(double currentCps, double elapsedSeconds)
        {
            Stats.SessionCurrentCps = currentCps;
            Stats.SessionActiveSeconds += elapsedSeconds;
            Stats.TotalActiveSecondsLifetime += (long)elapsedSeconds;

            if (currentCps > Stats.SessionPeakCps)
            {
                Stats.SessionPeakCps = currentCps;
            }

            if (currentCps > Stats.PeakCpsLifetime)
            {
                Stats.PeakCpsLifetime = currentCps;
            }

            StatisticsUpdated?.Invoke(Stats);
        }

        public void ResetStatistics()
        {
            Stats.TotalClicksLifetime = 0;
            Stats.TotalActiveSecondsLifetime = 0;
            Stats.PeakCpsLifetime = 0;
            Stats.MacrosExecutedLifetime = 0;
            Stats.SessionClicks = 0;
            Stats.SessionActiveSeconds = 0;
            Stats.SessionCurrentCps = 0;
            Stats.SessionPeakCps = 0;
            Stats.SessionMacrosExecuted = 0;
            Stats.LastResetDate = DateTime.UtcNow;

            SaveStatistics();
            StatisticsUpdated?.Invoke(Stats);
        }
    }
}
