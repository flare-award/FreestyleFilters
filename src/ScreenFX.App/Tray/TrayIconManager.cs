using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenFX.Tray
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;

        public event Action? ShowPanelRequested;
        public event Action? ToggleFiltersRequested;
        public event Action? ExitRequested;

        public TrayIconManager()
        {
            _menu = new ContextMenuStrip();
            _menu.Items.Add("Показать панель (Alt+Shift+F10)", null, (s,e)=> ShowPanelRequested?.Invoke());
            _menu.Items.Add("Вкл/Откл фильтры (Alt+Shift+F9)", null, (s,e)=> ToggleFiltersRequested?.Invoke());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Выход", null, (s,e)=> ExitRequested?.Invoke());

            _icon = new NotifyIcon
            {
                Text = "ScreenFX — системные фильтры",
                Visible = true,
                ContextMenuStrip = _menu,
                BalloonTipTitle = "ScreenFX",
            };
            // Try load icon from assets
            try
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (System.IO.File.Exists(icoPath)) _icon.Icon = new Icon(icoPath);
                else _icon.Icon = SystemIcons.Application;
            }
            catch { _icon.Icon = SystemIcons.Application; }

            _icon.DoubleClick += (s,e)=> ShowPanelRequested?.Invoke();
        }

        public void ShowBalloon(string text, string title = "ScreenFX", ToolTipIcon icon = ToolTipIcon.Info)
        {
            _icon.BalloonTipText = text;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(3000);
        }

        public void UpdateTooltip(bool filtersEnabled)
        {
            string state = filtersEnabled ? "Фильтры включены" : "Фильтры отключены";
            _icon.Text = $"ScreenFX — {state}\nAlt+Shift+F10 — панель";
            if (_icon.Text.Length > 63) _icon.Text = _icon.Text.Substring(0,63);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
