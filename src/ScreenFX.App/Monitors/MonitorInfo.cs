using System;
using System.Runtime.InteropServices;

namespace ScreenFX.Monitors
{
    public class MonitorInfo
    {
        public IntPtr Handle { get; set; }
        public string DeviceName { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public int DpiX { get; set; } = 96;
        public int DpiY { get; set; } = 96;
        public double ScaleFactor => DpiX / 96.0;
        public int Index { get; set; }

        // Уникальный идентификатор, не зависящий от порядка
        public string StableId => $"{DeviceName}_{Left}_{Top}_{Width}x{Height}";

        public string DisplayText
        {
            get
            {
                string primary = IsPrimary ? " (Основной)" : "";
                return $"{FriendlyName} — {Width}×{Height} @ ({Left},{Top}){primary}";
            }
        }

        public override string ToString() => DisplayText;
    }
}
