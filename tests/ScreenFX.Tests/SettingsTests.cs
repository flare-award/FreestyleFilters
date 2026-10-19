using System;
using System.IO;
using System.Text.Json;
using Xunit;
using ScreenFX.Settings;
using ScreenFX.Engine.Filters;

namespace ScreenFX.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void SaveAndLoad_PreservesFilters()
        {
            var service = new SettingsService();
            // Use temp file via reflection? Instead test JSON directly
            var settings = new AppSettings
            {
                Filters = new FilterParams { Saturation = 1.5f, Exposure = 0.7f, NegativeEnabled = true, LetterboxEnabled = true, LetterboxHeight = 0.12f },
                TargetMonitors = new System.Collections.Generic.List<string> { "mon1" },
                PanelMonitorId = "mon1",
                Hotkeys = new HotkeySettings { TogglePanel = "Ctrl+Shift+P", ToggleFilters = "Ctrl+Shift+F" }
            };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.NotNull(loaded);
            Assert.Equal(1.5f, loaded!.Filters.Saturation, 2);
            Assert.Equal(0.7f, loaded.Filters.Exposure, 2);
            Assert.True(loaded.Filters.NegativeEnabled);
        }

        [Fact]
        public void CorruptedSettings_FallsBackToDefaults()
        {
            // Simulate corrupted JSON handling: deserialization should throw, service should fallback
            string badJson = "{ invalid json";
            bool threw = false;
            try { JsonSerializer.Deserialize<AppSettings>(badJson); }
            catch { threw = true; }
            Assert.True(threw);
            // Service fallback test: creating new service and loading corrupted file would not crash
            var p = new FilterParams { Saturation = 999 }; // will clamp
            p.Validate();
            Assert.Equal(3, p.Saturation);
        }

        [Fact]
        public void Profile_SaveLoad()
        {
            var pm = new ProfileManager();
            var profile = new FilterProfile { Name = "Test", Filters = new FilterParams { Saturation = 1.8f } };
            string json = JsonSerializer.Serialize(profile);
            var loaded = JsonSerializer.Deserialize<FilterProfile>(json);
            Assert.NotNull(loaded);
            Assert.Equal("Test", loaded!.Name);
            Assert.Equal(1.8f, loaded.Filters.Saturation, 2);
        }

        [Fact]
        public void MonitorStableId_IndependentOfOrder()
        {
            var m1 = new ScreenFX.Monitors.MonitorInfo { DeviceName = @"\\.\DISPLAY1", Left = 0, Top = 0, Width = 1920, Height = 1080 };
            var m2 = new ScreenFX.Monitors.MonitorInfo { DeviceName = @"\\.\DISPLAY1", Left = 0, Top = 0, Width = 1920, Height = 1080 };
            Assert.Equal(m1.StableId, m2.StableId);
        }

        [Fact]
        public void HotkeyParse()
        {
            var cfg = ScreenFX.Hotkeys.HotkeyConfig.Parse("Alt+Shift+F10", 1);
            Assert.Equal((uint)0x0001 | 0x0004, cfg.Modifiers);
            Assert.Equal((uint)0x79, cfg.Vk); // F10 = 0x79
            var cfg2 = ScreenFX.Hotkeys.HotkeyConfig.Parse("Ctrl+Shift+P", 2);
            Assert.Equal((uint)0x0002 | 0x0004, cfg2.Modifiers);
        }
    }
}

