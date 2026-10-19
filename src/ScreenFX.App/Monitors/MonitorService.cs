using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms; // for Screen

namespace ScreenFX.Monitors
{
    public class MonitorService
    {
        public event Action? MonitorsChanged;

        // P/Invoke
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);
        private delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private const int MONITORINFOF_PRIMARY = 1;
        private const int MDT_EFFECTIVE_DPI = 0;

        public List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    var info = new MonitorInfo
                    {
                        Handle = hMon,
                        DeviceName = mi.szDevice,
                        FriendlyName = GetFriendlyName(mi.szDevice, list.Count),
                        Left = mi.rcMonitor.left,
                        Top = mi.rcMonitor.top,
                        Width = mi.rcMonitor.right - mi.rcMonitor.left,
                        Height = mi.rcMonitor.bottom - mi.rcMonitor.top,
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        Index = list.Count
                    };
                    try
                    {
                        if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0)
                        {
                            info.DpiX = (int)dx;
                            info.DpiY = (int)dy;
                        }
                    }
                    catch { }
                    // fallback to Screen class for DPI if shcore fails
                    list.Add(info);
                }
                return true;
            }, IntPtr.Zero);
            // Fallback via WinForms Screen if enumeration fails
            if (list.Count == 0)
            {
                int i = 0;
                foreach (var s in Screen.AllScreens)
                {
                    list.Add(new MonitorInfo
                    {
                        Handle = IntPtr.Zero,
                        DeviceName = s.DeviceName,
                        FriendlyName = $"Экран {i+1}",
                        Left = s.Bounds.Left,
                        Top = s.Bounds.Top,
                        Width = s.Bounds.Width,
                        Height = s.Bounds.Height,
                        IsPrimary = s.Primary,
                        Index = i++
                    });
                }
            }
            return list;
        }

        private string GetFriendlyName(string deviceName, int index)
        {
            // deviceName like \\.\DISPLAY1
            // Friendly: Экран 1, 2...
            // Можно попытаться получить через DisplayConfig, но для MVP — нумерация
            return $"Экран {index + 1} ({deviceName})";
        }

        public MonitorInfo? FindById(string stableId)
        {
            foreach (var m in GetMonitors())
                if (m.StableId == stableId) return m;
            return null;
        }

        public MonitorInfo? GetPrimary()
        {
            foreach (var m in GetMonitors())
                if (m.IsPrimary) return m;
            var all = GetMonitors();
            return all.Count > 0 ? all[0] : null;
        }

        // Вызывается из WndProc при WM_DISPLAYCHANGE
        public void NotifyDisplayChange()
        {
            MonitorsChanged?.Invoke();
        }
    }
}
