using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScreenFX.Engine.Filters;

namespace ScreenFX.Settings
{
    public class FilterProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Новый профиль";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public FilterParams Filters { get; set; } = new FilterParams();
        public List<string> TargetMonitors { get; set; } = new();
    }

    public class ProfileManager
    {
        private readonly string _profilesDir;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public ProfileManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _profilesDir = Path.Combine(appData, "ScreenFX", "profiles");
            Directory.CreateDirectory(_profilesDir);
        }

        public string ProfilesDir => _profilesDir;

        public List<FilterProfile> LoadAll()
        {
            var list = new List<FilterProfile>();
            if (!Directory.Exists(_profilesDir)) return list;
            foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var p = JsonSerializer.Deserialize<FilterProfile>(json, _opts);
                    if (p != null)
                    {
                        p.Filters.Validate();
                        list.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load profile {file}: {ex.Message}");
                    // skip corrupted
                }
            }
            return list.OrderBy(p => p.Name).ToList();
        }

        public void Save(FilterProfile profile)
        {
            profile.UpdatedAt = DateTime.Now;
            profile.Filters.Validate();
            string path = Path.Combine(_profilesDir, profile.Id + ".json");
            string json = JsonSerializer.Serialize(profile, _opts);
            File.WriteAllText(path, json);
        }

        public void Delete(string id)
        {
            string path = Path.Combine(_profilesDir, id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        public FilterProfile? Get(string id)
        {
            string path = Path.Combine(_profilesDir, id + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<FilterProfile>(File.ReadAllText(path), _opts);
            }
            catch { return null; }
        }

        public FilterProfile CreateFromCurrent(string name, FilterParams current, List<string> monitors)
        {
            var p = new FilterProfile { Name = name, Filters = current.DeepClone(), TargetMonitors = new List<string>(monitors) };
            Save(p);
            return p;
        }
    }
}
