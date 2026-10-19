using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ScreenFX.Hotkeys
{
    public class HotkeyConfig
    {
        public int Id { get; set; }
        public uint Modifiers { get; set; }
        public uint Vk { get; set; }
        public string Display { get; set; } = "";
        public static HotkeyConfig Parse(string display, int id)
        {
            // Display like "Alt+Shift+F10"
            uint mods = 0; uint vk = 0;
            var parts = display.Split('+');
            foreach (var p in parts)
            {
                var t = p.Trim().ToLowerInvariant();
                if (t == "alt") mods |= 0x0001; // MOD_ALT
                else if (t == "ctrl" || t == "control") mods |= 0x0002;
                else if (t == "shift") mods |= 0x0004;
                else if (t == "win") mods |= 0x0008;
                else
                {
                    // VK
                    if (t.StartsWith("f") && int.TryParse(t.Substring(1), out int fnum) && fnum >=1 && fnum <=24)
                        vk = (uint)(0x70 + fnum -1);
                    else if (t.Length==1) vk = (uint)char.ToUpperInvariant(t[0]);
                    else if (t == "space") vk = 0x20;
                }
            }
            return new HotkeyConfig { Id=id, Modifiers=mods, Vk=vk, Display=display };
        }
    }

    public class GlobalHotkeyManager : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private HwndSource? _source;
        private readonly Dictionary<int, Action> _handlers = new();
        private bool _disposed;

        public event Action<string>? HotkeyConflict;

        public void Initialize(HwndSource source)
        {
            _source = source;
            _source.AddHook(WndProc);
        }

        public bool Register(HotkeyConfig cfg, Action handler)
        {
            if (_source == null) return false;
            // Try register, handle conflict
            if (!RegisterHotKey(_source.Handle, cfg.Id, cfg.Modifiers, cfg.Vk))
            {
                int err = Marshal.GetLastWin32Error();
                HotkeyConflict?.Invoke($"Конфликт горячих клавиш {cfg.Display} (ошибка {err}). Попробуйте другое сочетание.");
                return false;
            }
            _handlers[cfg.Id] = handler;
            return true;
        }

        public void Unregister(int id)
        {
            if (_source != null) UnregisterHotKey(_source.Handle, id);
            _handlers.Remove(id);
        }

        public void ReRegister(HotkeyConfig oldCfg, HotkeyConfig newCfg, Action handler)
        {
            Unregister(oldCfg.Id);
            Register(newCfg, handler);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_handlers.TryGetValue(id, out var h))
                {
                    h();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null)
            {
                foreach (var id in new List<int>(_handlers.Keys))
                    UnregisterHotKey(_source.Handle, id);
                _source.RemoveHook(WndProc);
            }
            _handlers.Clear();
        }
    }
}
