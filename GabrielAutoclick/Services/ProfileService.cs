using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GabrielAutoclick.Helpers;
using GabrielAutoclick.Models;

namespace GabrielAutoclick.Services
{
    public class ProfileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public List<Profile> Profiles { get; private set; } = new List<Profile>();
        public Profile? ActiveProfile { get; private set; }

        public event Action<Profile>? ActiveProfileChanged;
        public event Action? ProfileListChanged;

        public ProfileService()
        {
            LoadProfiles();
        }

        public void LoadProfiles()
        {
            try
            {
                if (File.Exists(Constants.ProfilesFilePath))
                {
                    string json = File.ReadAllText(Constants.ProfilesFilePath);
                    var loaded = JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions);
                    if (loaded != null && loaded.Count > 0)
                    {
                        Profiles = loaded;
                        ActiveProfile = Profiles.First();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load profiles: {ex.Message}");
            }

            // Create default profiles if empty
            Profiles = CreateDefaultProfiles();
            ActiveProfile = Profiles.First();
            SaveProfiles();
        }

        public void SaveProfiles()
        {
            try
            {
                if (!Directory.Exists(Constants.AppDataFolder))
                {
                    Directory.CreateDirectory(Constants.AppDataFolder);
                }

                string json = JsonSerializer.Serialize(Profiles, JsonOptions);
                File.WriteAllText(Constants.ProfilesFilePath, json);
                ProfileListChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save profiles: {ex.Message}");
            }
        }

        public void SetActiveProfile(Profile profile)
        {
            if (profile == null) return;
            ActiveProfile = profile;
            ActiveProfileChanged?.Invoke(profile);
        }

        public Profile CreateProfile(string name, string description = "")
        {
            var profile = new Profile
            {
                Name = name,
                Description = description,
                Settings = new ClickSettings()
            };
            Profiles.Add(profile);
            SaveProfiles();
            SetActiveProfile(profile);
            return profile;
        }

        public Profile DuplicateProfile(Profile source, string newName)
        {
            var clone = source.Clone(newName);
            Profiles.Add(clone);
            SaveProfiles();
            SetActiveProfile(clone);
            return clone;
        }

        public bool DeleteProfile(Profile profile)
        {
            if (Profiles.Count <= 1) return false; // Keep at least one

            bool removed = Profiles.Remove(profile);
            if (removed)
            {
                if (ActiveProfile == profile)
                {
                    ActiveProfile = Profiles.FirstOrDefault();
                    if (ActiveProfile != null)
                    {
                        ActiveProfileChanged?.Invoke(ActiveProfile);
                    }
                }
                SaveProfiles();
            }
            return removed;
        }

        public string ExportProfileJson(Profile profile)
        {
            return JsonSerializer.Serialize(profile, JsonOptions);
        }

        public Profile? ImportProfileJson(string json)
        {
            try
            {
                var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions);
                if (profile != null)
                {
                    profile.Id = Guid.NewGuid().ToString();
                    profile.Name = $"{profile.Name} (Importado)";
                    Profiles.Add(profile);
                    SaveProfiles();
                    SetActiveProfile(profile);
                    return profile;
                }
            }
            catch { }
            return null;
        }

        private List<Profile> CreateDefaultProfiles()
        {
            var list = new List<Profile>();

            // 1. Blox Fruits
            var bloxFruits = new Profile
            {
                Name = "Blox Fruits (Farm)",
                Description = "Farm automático rápido para Roblox Blox Fruits com Follow Cursor a 12 CPS",
                Settings = new ClickSettings
                {
                    Milliseconds = 83, // ~12 CPS
                    MouseButton = MouseButtonType.Left,
                    PositionMode = ClickPositionMode.FollowCursor,
                    RepeatMode = RepeatMode.UntilStopped
                }
            };
            list.Add(bloxFruits);

            // 2. Minecraft PvP (Butterfly / Jitter simulation)
            var minecraft = new Profile
            {
                Name = "Minecraft PvP",
                Description = "Cliques rápidos com randomização de intervalo para evitar detecção (14-18 CPS)",
                Settings = new ClickSettings
                {
                    Milliseconds = 60,
                    MouseButton = MouseButtonType.Left,
                    PositionMode = ClickPositionMode.FollowCursor,
                    RandomIntervalEnabled = true,
                    MinRandomIntervalMs = 55,
                    MaxRandomIntervalMs = 75,
                    RandomPositionEnabled = true,
                    RandomRadiusPixels = 2
                }
            };
            list.Add(minecraft);

            // 3. Fast Clicker (20 CPS)
            var fast = new Profile
            {
                Name = "Velocidade Máxima (20 CPS)",
                Description = "20 cliques por segundo constantes",
                Settings = new ClickSettings
                {
                    Milliseconds = 50,
                    MouseButton = MouseButtonType.Left,
                    PositionMode = ClickPositionMode.FollowCursor
                }
            };
            list.Add(fast);

            // 4. Trabalho / Auto-Refresh
            var work = new Profile
            {
                Name = "Trabalho (1 click / 5s)",
                Description = "Clique espaçado a cada 5 segundos em ponto fixo",
                Settings = new ClickSettings
                {
                    Seconds = 5,
                    Milliseconds = 0,
                    MouseButton = MouseButtonType.Left,
                    PositionMode = ClickPositionMode.FixedPosition,
                    FixedX = 500,
                    FixedY = 500
                }
            };
            list.Add(work);

            return list;
        }
    }
}
