using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFX.Engine.Filters;

namespace ScreenFX.Settings
{
    public class HotkeySettings
    {
        public string TogglePanel { get; set; } = "Alt+Shift+F10";
        public string ToggleFilters { get; set; } = "Alt+Shift+F9";
        public string ResetFilters { get; set; } = "Alt+Shift+F8";
    }

    public class AppSettings
    {
        public FilterParams Filters { get; set; } = new FilterParams();
        public List<string> TargetMonitors { get; set; } = new(); // StableIds
        public string PanelMonitorId { get; set; } = ""; // StableId or "" = primary
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
        public bool StartMinimized { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public string Version { get; set; } = "1.0.0";
        public bool FirstRun { get; set; } = true;
    }

    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly string _settingsDir;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public AppSettings Current { get; private set; } = new();

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsDir = Path.Combine(appData, "ScreenFX");
            _settingsPath = Path.Combine(_settingsDir, "settings.json");
        }

        public string SettingsPath => _settingsPath;

        public void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    Current = new AppSettings();
                    Save(); // create default
                    return;
                }
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded == null) throw new Exception("Null settings");
                loaded.Filters.Validate();
                // Validate hotkeys strings not empty
                if (string.IsNullOrWhiteSpace(loaded.Hotkeys.TogglePanel)) loaded.Hotkeys.TogglePanel = "Alt+Shift+F10";
                if (string.IsNullOrWhiteSpace(loaded.Hotkeys.ToggleFilters)) loaded.Hotkeys.ToggleFilters = "Alt+Shift+F9";
                Current = loaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings load failed, using defaults: {ex.Message}");
                // Backup corrupted file
                try
                {
                    if (File.Exists(_settingsPath))
                        File.Copy(_settingsPath, _settingsPath + ".corrupted." + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                } catch {}
                Current = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                string json = JsonSerializer.Serialize(Current, _jsonOptions);
                // Atomic write
                string tmp = _settingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _settingsPath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex}");
            }
        }

        public void ResetToDefaults()
        {
            Current = new AppSettings();
            Save();
        }
    }
}
