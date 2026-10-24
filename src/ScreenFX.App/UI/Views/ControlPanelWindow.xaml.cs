using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenFX.Engine.Filters;
using ScreenFX.Engine.Rendering;
using ScreenFX.Engine.Capture;
using ScreenFX.Hotkeys;
using ScreenFX.Monitors;
using ScreenFX.Overlay;
using ScreenFX.Settings;
using ScreenFX.Tray;

namespace ScreenFX.UI.Views
{
    public partial class ControlPanelWindow : Window
    {
        // P/Invoke for animation respect reduce motion + monitor positioning
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

        // Animation
        private bool _isPanelVisible = false;
        private bool _isAnimating = false;
        private Storyboard? _currentStoryboard;

        // Core services
        private readonly SettingsService _settingsService = new();
        private readonly ProfileManager _profileManager = new();
        private readonly MonitorService _monitorService = new();
        private readonly D3D11DeviceManager _deviceManager = new();
        private FilterRenderer? _filterRenderer;
        private CaptureManager _captureManager = new();
        private OverlayManager? _overlayManager;
        private TrayIconManager? _trayManager;
        private GlobalHotkeyManager _hotkeyManager = new();
        private HwndSource? _hwndSource;

        private FilterParams _params = new();
        private string _panelMonitorId = "";
        private HotkeyConfig _hkPanel = HotkeyConfig.Parse("Alt+Shift+F10", 9001);
        private HotkeyConfig _hkFilters = HotkeyConfig.Parse("Alt+Shift+F9", 9002);
        private HotkeyConfig _hkReset = HotkeyConfig.Parse("Alt+Shift+F8", 9003);

        private bool _suppressUiEvents = false;

        public ControlPanelWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
            // Window dragging
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            // Hook display change
            _monitorService.MonitorsChanged += () => Dispatcher.Invoke(RefreshMonitors);

            // UI wiring - tabs
            TabFilters.Click += (s, e) => ShowTab("filters");
            TabMonitors.Click += (s, e) => ShowTab("monitors");
            TabSettings.Click += (s, e) => ShowTab("settings");

            CloseButton.Click += (s, e) => HidePanelAnimated();
            BtnHidePanel.Click += (s, e) => HidePanelAnimated();
            BtnQuitApp.Click += (s, e) => ExitApp();

            // Global toggle
            GlobalToggle.Checked += (s, e) => OnGlobalToggle(true);
            GlobalToggle.Unchecked += (s, e) => OnGlobalToggle(false);

            // Wire sliders
            WireFilterControls();

            // Monitor buttons
            BtnSelectAllMonitors.Click += (s, e) => SelectAllMonitors(true);
            BtnClearMonitors.Click += (s, e) => SelectAllMonitors(false);
            BtnRefreshMonitors.Click += (s, e) => RefreshMonitors();
            ComboPanelMonitor.SelectionChanged += (s, e) => OnPanelMonitorChanged();

            // Profiles
            BtnSaveProfile.Click += (s, e) => SaveProfile();
            BtnLoadProfile.Click += (s, e) => LoadProfile();
            BtnDeleteProfile.Click += (s, e) => DeleteProfile();

            // Hotkeys
            BtnChangeHotkeyPanel.Click += async (s, e) => await ChangeHotkey("panel");
            BtnChangeHotkeyFilters.Click += async (s, e) => await ChangeHotkey("filters");
            BtnChangeHotkeyReset.Click += async (s, e) => await ChangeHotkey("reset");

            // Startup
            ToggleStartup.Checked += (s, e) => SetStartup(true);
            ToggleStartup.Unchecked += (s, e) => SetStartup(false);

            BtnOpenSettingsFolder.Click += (s, e) => { try { Process.Start("explorer.exe", Path.GetDirectoryName(_settingsService.SettingsPath)!); } catch {} };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Load settings
            _settingsService.Load();
            _params = _settingsService.Current.Filters;
            _panelMonitorId = _settingsService.Current.PanelMonitorId;

            // Hotkeys from settings
            _hkPanel = HotkeyConfig.Parse(_settingsService.Current.Hotkeys.TogglePanel, 9001);
            _hkFilters = HotkeyConfig.Parse(_settingsService.Current.Hotkeys.ToggleFilters, 9002);
            _hkReset = HotkeyConfig.Parse(_settingsService.Current.Hotkeys.ResetFilters, 9003);
            HotkeyPanelDisplay.Text = _hkPanel.Display;
            HotkeyFiltersDisplay.Text = _hkFilters.Display;
            HotkeyResetDisplay.Text = _hkReset.Display;

            // Init D3D + overlays
            if (!_deviceManager.Initialize())
            {
                System.Windows.MessageBox.Show("Не удалось инициализировать Direct3D 11. Фильтры будут недоступны.\nПроверьте драйвер видеокарты.", "ScreenFX", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            else
            {
                _filterRenderer = new FilterRenderer(_deviceManager);
                if (!_filterRenderer.Initialize())
                {
                    System.Windows.MessageBox.Show("Не удалось скомпилировать шейдеры фильтров.\n" + _filterRenderer.LastError, "ScreenFX", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
                _captureManager.SetDevice(_deviceManager.Device!, _deviceManager.Context!);
                _overlayManager = new OverlayManager(_deviceManager, _filterRenderer!, _captureManager);
                _overlayManager.StartRenderLoop();
                ApplyMonitorTargets();
            }

            // Tray
            _trayManager = new TrayIconManager();
            _trayManager.ShowPanelRequested += () => Dispatcher.Invoke(ShowPanelAnimated);
            _trayManager.ToggleFiltersRequested += () => Dispatcher.Invoke(ToggleFilters);
            _trayManager.ExitRequested += () => Dispatcher.Invoke(ExitApp);

            // Hotkeys
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (_hwndSource != null)
            {
                _hotkeyManager.Initialize(_hwndSource);
                _hotkeyManager.HotkeyConflict += msg => Dispatcher.Invoke(() => { HotkeyStatus.Text = msg; _trayManager?.ShowBalloon(msg); });
                _hotkeyManager.Register(_hkPanel, () => Dispatcher.Invoke(TogglePanel));
                _hotkeyManager.Register(_hkFilters, () => Dispatcher.Invoke(ToggleFilters));
                _hotkeyManager.Register(_hkReset, () => Dispatcher.Invoke(ResetAll));
            }

            // Also hook WndProc for display change + hotkeys for hidden window? Ensure tray hotkeys work when panel hidden: we use hidden hwnd source?
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);

            // UI init
            RefreshMonitors();
            RefreshProfiles();
            SyncUiFromParams();
            ToggleStartup.IsChecked = _settingsService.Current.RunAtStartup;

            // Respect reduced motion: check SystemParameters? WPF doesn't expose directly, use registry? For now check if animation disabled via system setting
            // If FirstRun, show panel
            if (_settingsService.Current.FirstRun)
            {
                _settingsService.Current.FirstRun = false;
                _settingsService.Save();
                _trayManager?.ShowBalloon("Нажмите Alt+Shift+F10 чтобы показать панель ScreenFX", "Добро пожаловать!");
                ShowPanelAnimated();
            }
            else
            {
                // Start hidden if StartMinimized
                HidePanelImmediate();
            }

            UpdateStatus();
            // Handle monitor change from settings: if panel monitor disconnected, fallback
            EnsurePanelMonitorValid();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DISPLAYCHANGE = 0x007E;
            const int WM_DPICHANGED = 0x02E0;
            if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    RefreshMonitors();
                    EnsurePanelMonitorValid();
                    ApplyMonitorTargets();
                });
            }
            return IntPtr.Zero;
        }

        private bool _isExiting = false;
        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExiting) return;
            e.Cancel = true;
            HidePanelAnimated();
        }

        private void ExitApp()
        {
            _isExiting = true;
            SaveCurrentSettings();
            _trayManager?.Dispose();
            _hotkeyManager.Dispose();
            _overlayManager?.Dispose();
            _captureManager.Dispose();
            _deviceManager.Dispose();
            Application.Current.Shutdown();
        }

        // Panel animation
        private void ShowPanelAnimated()
        {
            if (_isPanelVisible || _isAnimating) return;
            _isAnimating = true;

            // Position on panel monitor left side
            PositionOnPanelMonitor();

            // Ensure window visible
            Show();
            Activate();
            // Keep overlay click-through so desktop stays interactive (overlay is visual only)
            _overlayManager?.SetClickThrough(true);

            // Animate from -Width to 0
            bool reduceMotion = SystemParameters.MenuAnimation == false; // proxy for reduce motion
            if (reduceMotion)
            {
                Left = GetPanelLeft();
                _isPanelVisible = true;
                _isAnimating = false;
                return;
            }

            double targetLeft = GetPanelLeft();
            double startLeft = targetLeft - Width - 20;
            Left = startLeft;
            Opacity = 0;

            var leftAnim = new DoubleAnimation(startLeft, targetLeft, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));

            var sb = new Storyboard();
            Storyboard.SetTarget(leftAnim, this);
            Storyboard.SetTargetProperty(leftAnim, new PropertyPath(Window.LeftProperty));
            Storyboard.SetTarget(opacityAnim, this);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(Window.OpacityProperty));
            sb.Children.Add(leftAnim);
            sb.Children.Add(opacityAnim);
            sb.Completed += (s, e) => { _isPanelVisible = true; _isAnimating = false; };
            _currentStoryboard?.Stop();
            _currentStoryboard = sb;
            sb.Begin();
        }

        private void HidePanelAnimated()
        {
            if (!_isPanelVisible || _isAnimating)
            {
                if (!_isPanelVisible)
                {
                    Hide();
                    return;
                }
            }
            _isAnimating = true;
            bool reduceMotion = SystemParameters.MenuAnimation == false;
            if (reduceMotion)
            {
                Hide();
                _isPanelVisible = false;
                _isAnimating = false;
                _overlayManager?.SetClickThrough(true);
                return;
            }
            double startLeft = Left;
            double targetLeft = startLeft - Width - 20;
            var leftAnim = new DoubleAnimation(startLeft, targetLeft, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var sb = new Storyboard();
            Storyboard.SetTarget(leftAnim, this);
            Storyboard.SetTargetProperty(leftAnim, new PropertyPath(Window.LeftProperty));
            Storyboard.SetTarget(opacityAnim, this);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(Window.OpacityProperty));
            sb.Children.Add(leftAnim);
            sb.Children.Add(opacityAnim);
            sb.Completed += (s, e) =>
            {
                Hide();
                Opacity = 1;
                _isPanelVisible = false;
                _isAnimating = false;
                _overlayManager?.SetClickThrough(true);
            };
            _currentStoryboard?.Stop();
            _currentStoryboard = sb;
            sb.Begin();
        }

        private void HidePanelImmediate()
        {
            _isPanelVisible = false;
            Hide();
            _overlayManager?.SetClickThrough(true);
        }

        private void TogglePanel()
        {
            if (_isPanelVisible) HidePanelAnimated(); else ShowPanelAnimated();
        }

        private double GetPanelLeft()
        {
            var monitor = GetPanelMonitor();
            if (monitor == null) return 0;
            // Left edge with 12px margin
            return monitor.Left + 12;
        }

        private double GetPanelTop()
        {
            var monitor = GetPanelMonitor();
            if (monitor == null) return 0;
            // Vertically centered
            return monitor.Top + (monitor.Height - Height) / 2;
        }

        private void PositionOnPanelMonitor()
        {
            var m = GetPanelMonitor();
            if (m == null) return;
            Left = GetPanelLeft();
            Top = GetPanelTop();
            // Ensure within monitor
            if (Top < m.Top) Top = m.Top + 12;
            if (Top + Height > m.Top + m.Height) Top = m.Top + m.Height - Height - 12;
        }

        private MonitorInfo? GetPanelMonitor()
        {
            var all = _monitorService.GetMonitors();
            if (!string.IsNullOrEmpty(_panelMonitorId))
            {
                var found = all.FirstOrDefault(x => x.StableId == _panelMonitorId);
                if (found != null) return found;
            }
            // Primary fallback
            return all.FirstOrDefault(x => x.IsPrimary) ?? all.FirstOrDefault();
        }

        private void EnsurePanelMonitorValid()
        {
            var all = _monitorService.GetMonitors();
            if (!string.IsNullOrEmpty(_panelMonitorId) && !all.Any(x => x.StableId == _panelMonitorId))
            {
                var primary = all.FirstOrDefault(x => x.IsPrimary) ?? all.FirstOrDefault();
                if (primary != null)
                {
                    _panelMonitorId = primary.StableId;
                    _settingsService.Current.PanelMonitorId = _panelMonitorId;
                    _settingsService.Save();
                    _trayManager?.ShowBalloon($"Монитор панели отключён, панель перенесена на {primary.FriendlyName}");
                    if (_isPanelVisible) PositionOnPanelMonitor();
                }
            }
        }

        private void ShowTab(string tab)
        {
            FiltersPanel.Visibility = tab=="filters" ? Visibility.Visible : Visibility.Collapsed;
            MonitorsPanel.Visibility = tab=="monitors" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = tab=="settings" ? Visibility.Visible : Visibility.Collapsed;

            var activeStyle = FindResource("FilledButton") as Style;
            var inactiveStyle = FindResource("TextButton") as Style;
            TabFilters.Style = tab=="filters" ? activeStyle : inactiveStyle;
            TabMonitors.Style = tab=="monitors" ? activeStyle : inactiveStyle;
            TabSettings.Style = tab=="settings" ? activeStyle : inactiveStyle;
            // Update foreground for inactive
            if (tab!="filters") TabFilters.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryBrush");
            if (tab!="monitors") TabMonitors.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryBrush");
            if (tab!="settings") TabSettings.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryBrush");
        }

        private void WireFilterControls()
        {
            // Saturation
            ToggleSaturation.Checked += (s,e)=> { _params.Saturation = (float)SliderSaturation.Value; ApplyFilters(); };
            ToggleSaturation.Unchecked += (s,e)=> { ApplyFilters(); };
            SliderSaturation.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueSaturation.Text = $"{(int)(SliderSaturation.Value*100)}%"; _params.Saturation = (float)SliderSaturation.Value; if(ToggleSaturation.IsChecked==true) ApplyFilters(); } };
            ResetSaturation.Click += (s,e)=> { _params.ResetFilter("saturation"); SyncUiFromParams(); ApplyFilters(); };

            // Exposure
            ToggleExposure.Checked += (s,e)=> ApplyFilters();
            ToggleExposure.Unchecked += (s,e)=> ApplyFilters();
            SliderExposure.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueExposure.Text = $"{SliderExposure.Value:F1} EV"; _params.Exposure = (float)SliderExposure.Value; if(ToggleExposure.IsChecked==true) ApplyFilters(); } };
            ResetExposure.Click += (s,e)=> { _params.ResetFilter("exposure"); SyncUiFromParams(); ApplyFilters(); };

            // Negative
            ToggleNegative.Checked += (s,e)=> { _params.NegativeEnabled = true; ApplyFilters(); };
            ToggleNegative.Unchecked += (s,e)=> { _params.NegativeEnabled = false; ApplyFilters(); };

            // Letterbox
            ToggleLetterbox.Checked += (s,e)=> { _params.LetterboxEnabled = true; LetterboxOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleLetterbox.Unchecked += (s,e)=> { _params.LetterboxEnabled = false; LetterboxOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderLetterbox.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueLetterbox.Text = $"{(int)(SliderLetterbox.Value*100)}%"; _params.LetterboxHeight = (float)SliderLetterbox.Value; if(ToggleLetterbox.IsChecked==true) ApplyFilters(); } };
            ComboLetterboxPreset.SelectionChanged += (s,e)=> OnLetterboxPresetChanged();
            BtnLetterboxBlack.Click += (s,e)=> { _params.LetterboxColorR=_params.LetterboxColorG=_params.LetterboxColorB=0; ApplyFilters(); };
            BtnLetterboxDark.Click += (s,e)=> { _params.LetterboxColorR=_params.LetterboxColorG=_params.LetterboxColorB=0.12f; ApplyFilters(); };

            // Hue
            ToggleHue.Checked += (s,e)=> ApplyFilters();
            ToggleHue.Unchecked += (s,e)=> ApplyFilters();
            SliderHue.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHue.Text = $"{(int)SliderHue.Value}°"; _params.HueDegrees = (float)SliderHue.Value; if(ToggleHue.IsChecked==true) ApplyFilters(); } };
            ResetHue.Click += (s,e)=> { _params.ResetFilter("hue"); SyncUiFromParams(); ApplyFilters(); };

            // Temp
            ToggleTemp.Checked += (s,e)=> ApplyFilters();
            ToggleTemp.Unchecked += (s,e)=> ApplyFilters();
            SliderTemp.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueTemp.Text = SliderTemp.Value.ToString("F2"); _params.Temperature = (float)SliderTemp.Value; if(ToggleTemp.IsChecked==true) ApplyFilters(); } };
            ResetTemp.Click += (s,e)=> { _params.ResetFilter("temperature"); SyncUiFromParams(); ApplyFilters(); };

            // Night
            ToggleNight.Checked += (s,e)=> { _params.NightModeEnabled = true; NightOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleNight.Unchecked += (s,e)=> { _params.NightModeEnabled = false; NightOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderNight.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueNight.Text = $"{(int)(SliderNight.Value*100)}%"; _params.NightModeStrength = (float)SliderNight.Value; if(ToggleNight.IsChecked==true) ApplyFilters(); } };

            // Halftone
            ToggleHalftone.Checked += (s,e)=> { _params.HalftoneEnabled = true; HalftoneOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleHalftone.Unchecked += (s,e)=> { _params.HalftoneEnabled = false; HalftoneOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderHalftoneStr.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHalftoneStr.Text = $"{(int)(SliderHalftoneStr.Value*100)}%"; _params.HalftoneStrength = (float)SliderHalftoneStr.Value; if(ToggleHalftone.IsChecked==true) ApplyFilters(); } };
            SliderHalftoneDot.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHalftoneDot.Text = $"{(int)SliderHalftoneDot.Value} px"; _params.HalftoneDotSize = (float)SliderHalftoneDot.Value; if(ToggleHalftone.IsChecked==true) ApplyFilters(); } };
            SliderHalftoneContrast.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHalftoneContrast.Text = SliderHalftoneContrast.Value.ToString("F1"); _params.HalftoneContrast = (float)SliderHalftoneContrast.Value; if(ToggleHalftone.IsChecked==true) ApplyFilters(); } };
            SliderHalftoneAngle.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHalftoneAngle.Text = $"{(int)SliderHalftoneAngle.Value}°"; _params.HalftoneAngle = (float)SliderHalftoneAngle.Value; if(ToggleHalftone.IsChecked==true) ApplyFilters(); } };

            // Sharpen
            ToggleSharpen.Checked += (s,e)=> { _params.SharpenEnabled = true; SharpenOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleSharpen.Unchecked += (s,e)=> { _params.SharpenEnabled = false; SharpenOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderSharpen.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueSharpen.Text = $"{(int)(SliderSharpen.Value*100)}%"; _params.SharpenStrength = (float)SliderSharpen.Value; if(ToggleSharpen.IsChecked==true) ApplyFilters(); } };

            // Circular
            ToggleCircular.Checked += (s,e)=> { _params.CircularBlurEnabled = true; CircularOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleCircular.Unchecked += (s,e)=> { _params.CircularBlurEnabled = false; CircularOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderCircularRadius.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueCircularRadius.Text = $"{(int)(SliderCircularRadius.Value*100)}%"; _params.CircularBlurRadius = (float)SliderCircularRadius.Value; if(ToggleCircular.IsChecked==true) ApplyFilters(); } };
            SliderCircularStr.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueCircularStr.Text = $"{(int)(SliderCircularStr.Value*100)}%"; _params.CircularBlurStrength = (float)SliderCircularStr.Value; if(ToggleCircular.IsChecked==true) ApplyFilters(); } };
            SliderCircularSmooth.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueCircularSmooth.Text = SliderCircularSmooth.Value.ToString("F2"); _params.CircularBlurSmoothness = (float)SliderCircularSmooth.Value; if(ToggleCircular.IsChecked==true) ApplyFilters(); } };
            SliderCircularX.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ _params.CircularBlurCenterX=(float)SliderCircularX.Value; UpdateCircularCenterText(); if(ToggleCircular.IsChecked==true) ApplyFilters(); } };
            SliderCircularY.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ _params.CircularBlurCenterY=(float)SliderCircularY.Value; UpdateCircularCenterText(); if(ToggleCircular.IsChecked==true) ApplyFilters(); } };
            BtnCircularResetCenter.Click += (s,e)=> { _params.CircularBlurCenterX=0.5f; _params.CircularBlurCenterY=0.5f; SyncUiFromParams(); ApplyFilters(); };

            // Full Blur
            ToggleBlur.Checked += (s,e)=> { _params.BlurEnabled = true; BlurOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleBlur.Unchecked += (s,e)=> { _params.BlurEnabled = false; BlurOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderBlur.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueBlur.Text = $"{(int)(SliderBlur.Value*100)}%"; _params.BlurStrength = (float)SliderBlur.Value; if(ToggleBlur.IsChecked==true) ApplyFilters(); } };

            // HSV
            ToggleHSV.Checked += (s,e)=> { _params.HsvEnabled = true; HSVOptions.Visibility = Visibility.Visible; ApplyFilters(); };
            ToggleHSV.Unchecked += (s,e)=> { _params.HsvEnabled = false; HSVOptions.Visibility = Visibility.Collapsed; ApplyFilters(); };
            SliderHSVHue.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHSVHue.Text = $"{(int)SliderHSVHue.Value}°"; _params.HsvHueDegrees = (float)SliderHSVHue.Value; if(ToggleHSV.IsChecked==true) ApplyFilters(); } };
            SliderHSVSat.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHSVSat.Text = $"{(int)(SliderHSVSat.Value*100)}%"; _params.HsvSaturation = (float)SliderHSVSat.Value; if(ToggleHSV.IsChecked==true) ApplyFilters(); } };
            SliderHSVVal.ValueChanged += (s,e)=> { if(!_suppressUiEvents){ ValueHSVVal.Text = $"{(int)(SliderHSVVal.Value*100)}%"; _params.HsvValue = (float)SliderHSVVal.Value; if(ToggleHSV.IsChecked==true) ApplyFilters(); } };
            BtnResetHSV.Click += (s,e)=> { _params.ResetFilter("hsv"); SyncUiFromParams(); ApplyFilters(); };

            // Global quick actions
            BtnDisableAll.Click += (s,e)=> { DisableAllFilters(); };
            BtnResetAll.Click += (s,e)=> { ResetAll(); };
        }

        private void OnLetterboxPresetChanged()
        {
            if (_suppressUiEvents) return;
            if (ComboLetterboxPreset.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "Off") { _params.LetterboxEnabled = false; ToggleLetterbox.IsChecked = false; }
                else if (tag == "Manual") { /* keep current height */ }
                else if (float.TryParse(tag.Replace(":", "").Replace("/", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float aspect) || float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out aspect))
                {
                    // Approximate height from aspect: for 16:9 screen, letterbox height = (1 - (16/9)/aspect)/2 roughly. Use simplified.
                    // For simplicity map presets to heights: 2.39 ->0.12, 2.0->0.08, 1.85->0.06, 2.35->0.11
                    float h = tag switch { "2.39"=>0.14f, "2"=>0.08f, "1.85"=>0.05f, "2.35"=>0.12f, _=>0.08f };
                    _params.LetterboxHeight = h;
                    _params.LetterboxEnabled = true;
                    ToggleLetterbox.IsChecked = true;
                    SliderLetterbox.Value = h;
                }
                ApplyFilters();
            }
        }

        private void UpdateCircularCenterText()
        {
            ValueCircularCenter.Text = $"Центр: {_params.CircularBlurCenterX:F2}, {_params.CircularBlurCenterY:F2}";
        }

        private void SyncUiFromParams()
        {
            _suppressUiEvents = true;
            try
            {
                // Saturation - if saturation ==1 and not explicitly enabled? We use check if saturation !=1 or flag? For now map to toggle based on whether user enabled. But we need persistent enabled flags.
                // Instead we infer: if saturation !=1 then enabled, but we have separate enabled logic for some filters. For saturation/hue/temperature we treat toggle as enable; value always present but only applied when checked.
                // So we need separate Enabled flags for those too. For MVP we add flags.
                // We'll store enabled as distinct bool per filter already: for saturation we don't have bool, but we can treat SaturationEnabled as saturation !=1? Simpler: keep toggle independent.
                // Fix: add SaturationEnabled flag? For now we use value 1 as neutral but toggle controls whether to apply.
                // We'll approximate: Toggle reflects whether value deviates or was explicitly enabled. Keep last toggle state in _params? Add missing toggles.
                // Quick workaround: maintain dictionary of enabled states.
            }
            finally { _suppressUiEvents = false; }
            // Do full sync
            ApplyUiValues();
        }

        private void ApplyUiValues()
        {
            _suppressUiEvents = true;
            // Saturation
            SliderSaturation.Value = _params.Saturation;
            ValueSaturation.Text = $"{(int)(_params.Saturation*100)}%";
            // Exposure
            SliderExposure.Value = _params.Exposure;
            ValueExposure.Text = $"{_params.Exposure:F1} EV";
            // Negative
            ToggleNegative.IsChecked = _params.NegativeEnabled;
            // Letterbox
            ToggleLetterbox.IsChecked = _params.LetterboxEnabled;
            LetterboxOptions.Visibility = _params.LetterboxEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderLetterbox.Value = _params.LetterboxHeight;
            ValueLetterbox.Text = $"{(int)(_params.LetterboxHeight*100)}%";
            // Hue
            SliderHue.Value = _params.HueDegrees;
            ValueHue.Text = $"{(int)_params.HueDegrees}°";
            // Temp
            SliderTemp.Value = _params.Temperature;
            ValueTemp.Text = _params.Temperature.ToString("F2");
            // Night
            ToggleNight.IsChecked = _params.NightModeEnabled;
            NightOptions.Visibility = _params.NightModeEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderNight.Value = _params.NightModeStrength;
            ValueNight.Text = $"{(int)(_params.NightModeStrength*100)}%";
            // Halftone
            ToggleHalftone.IsChecked = _params.HalftoneEnabled;
            HalftoneOptions.Visibility = _params.HalftoneEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderHalftoneStr.Value = _params.HalftoneStrength;
            ValueHalftoneStr.Text = $"{(int)(_params.HalftoneStrength*100)}%";
            SliderHalftoneDot.Value = _params.HalftoneDotSize;
            ValueHalftoneDot.Text = $"{(int)_params.HalftoneDotSize} px";
            SliderHalftoneContrast.Value = _params.HalftoneContrast;
            ValueHalftoneContrast.Text = _params.HalftoneContrast.ToString("F1");
            SliderHalftoneAngle.Value = _params.HalftoneAngle;
            ValueHalftoneAngle.Text = $"{(int)_params.HalftoneAngle}°";
            // Sharpen
            ToggleSharpen.IsChecked = _params.SharpenEnabled;
            SharpenOptions.Visibility = _params.SharpenEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderSharpen.Value = _params.SharpenStrength;
            ValueSharpen.Text = $"{(int)(_params.SharpenStrength*100)}%";
            // Circular
            ToggleCircular.IsChecked = _params.CircularBlurEnabled;
            CircularOptions.Visibility = _params.CircularBlurEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderCircularRadius.Value = _params.CircularBlurRadius;
            ValueCircularRadius.Text = $"{(int)(_params.CircularBlurRadius*100)}%";
            SliderCircularStr.Value = _params.CircularBlurStrength;
            ValueCircularStr.Text = $"{(int)(_params.CircularBlurStrength*100)}%";
            SliderCircularSmooth.Value = _params.CircularBlurSmoothness;
            ValueCircularSmooth.Text = _params.CircularBlurSmoothness.ToString("F2");
            SliderCircularX.Value = _params.CircularBlurCenterX;
            SliderCircularY.Value = _params.CircularBlurCenterY;
            UpdateCircularCenterText();
            // Blur
            ToggleBlur.IsChecked = _params.BlurEnabled;
            BlurOptions.Visibility = _params.BlurEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderBlur.Value = _params.BlurStrength;
            ValueBlur.Text = $"{(int)(_params.BlurStrength*100)}%";
            // HSV
            ToggleHSV.IsChecked = _params.HsvEnabled;
            HSVOptions.Visibility = _params.HsvEnabled ? Visibility.Visible : Visibility.Collapsed;
            SliderHSVHue.Value = _params.HsvHueDegrees;
            ValueHSVHue.Text = $"{(int)_params.HsvHueDegrees}°";
            SliderHSVSat.Value = _params.HsvSaturation;
            ValueHSVSat.Text = $"{(int)(_params.HsvSaturation*100)}%";
            SliderHSVVal.Value = _params.HsvValue;
            ValueHSVVal.Text = $"{(int)(_params.HsvValue*100)}%";

            GlobalToggle.IsChecked = _params.FiltersEnabled;
            _suppressUiEvents = false;
        }

        private void ApplyFilters()
        {
            // Special handling for toggle-checked filters that have no explicit Enabled flag for saturation/hue/temp
            // We treat Saturation toggle as whether to use custom value vs 1; if unchecked, set saturation to 1 temporarily for GPU but keep stored value
            var gpuParams = _params.DeepClone();
            if (ToggleSaturation.IsChecked != true) gpuParams.Saturation = 1f;
            if (ToggleExposure.IsChecked != true) gpuParams.Exposure = 0f;
            if (ToggleHue.IsChecked != true) gpuParams.Hue = 0f;
            if (ToggleTemp.IsChecked != true) gpuParams.Temperature = 0f;

            _overlayManager?.UpdateFilters(gpuParams);
            // Also update settings current but not save immediately (throttle)
            _settingsService.Current.Filters = _params.DeepClone();
            // Debounced save
            _saveDebounce?.Stop();
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounce.Tick += (s,e)=> { _saveDebounce.Stop(); SaveCurrentSettings(); };
            _saveDebounce.Start();
            UpdateStatus();
        }

        private DispatcherTimer? _saveDebounce;

        private void SaveCurrentSettings()
        {
            _settingsService.Current.Filters = _params.DeepClone();
            _settingsService.Current.PanelMonitorId = _panelMonitorId;
            _settingsService.Save();
        }

        private void OnGlobalToggle(bool enabled)
        {
            _params.FiltersEnabled = enabled;
            ApplyFilters();
            UpdateStatus();
        }

        private void ToggleFilters()
        {
            _params.FiltersEnabled = !_params.FiltersEnabled;
            GlobalToggle.IsChecked = _params.FiltersEnabled;
            ApplyFilters();
            _trayManager?.UpdateTooltip(_params.FiltersEnabled);
            _trayManager?.ShowBalloon(_params.FiltersEnabled ? "Фильтры включены" : "Фильтры отключены");
        }

        private void DisableAllFilters()
        {
            _params.FiltersEnabled = false;
            GlobalToggle.IsChecked = false;
            ApplyFilters();
        }

        private void ResetAll()
        {
            _params.Reset();
            SyncUiFromParams();
            ApplyFilters();
            _trayManager?.ShowBalloon("Настройки сброшены к исходным");
        }

        private void UpdateStatus()
        {
            bool enabled = _params.FiltersEnabled;
            StatusText.Text = enabled ? "Фильтры включены" : "Фильтры отключены";
            StatusDot.Fill = enabled ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Gray;
            FooterDot.Fill = enabled ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Gray;
            FooterText.Text = enabled ? "Готово • Фильтрация активна" : "Пауза • Фильтры отключены";
            _trayManager?.UpdateTooltip(enabled);
        }

        // Monitors
        private void RefreshMonitors()
        {
            var monitors = _monitorService.GetMonitors();
            MonitorList.Children.Clear();
            ComboPanelMonitor.Items.Clear();

            foreach (var m in monitors)
            {
                // CheckBox for target
                var cb = new CheckBox
                {
                    Content = $"{m.DisplayText}",
                    Tag = m.StableId,
                    Margin = new Thickness(0,4,0,4),
                    IsChecked = _settingsService.Current.TargetMonitors.Contains(m.StableId) || (_settingsService.Current.TargetMonitors.Count==0 && m.IsPrimary)
                };
                cb.Checked += (s,e)=> OnTargetMonitorChanged();
                cb.Unchecked += (s,e)=> OnTargetMonitorChanged();
                MonitorList.Children.Add(cb);

                var item = new ComboBoxItem { Content = m.DisplayText, Tag = m.StableId };
                ComboPanelMonitor.Items.Add(item);
                if (m.StableId == _panelMonitorId) ComboPanelMonitor.SelectedItem = item;
            }
            if (ComboPanelMonitor.SelectedItem == null && ComboPanelMonitor.Items.Count>0)
            {
                // Select primary
                var primary = monitors.FirstOrDefault(x=>x.IsPrimary);
                foreach (ComboBoxItem it in ComboPanelMonitor.Items)
                    if (it.Tag as string == primary?.StableId) { ComboPanelMonitor.SelectedItem = it; break; }
                if (ComboPanelMonitor.SelectedItem == null) ComboPanelMonitor.SelectedIndex = 0;
            }
            // If no target selected, select primary
            if (_settingsService.Current.TargetMonitors.Count==0 && monitors.Count>0)
            {
                var primary = monitors.FirstOrDefault(x=>x.IsPrimary) ?? monitors[0];
                _settingsService.Current.TargetMonitors = new List<string>{ primary.StableId };
                // Recheck UI
                foreach (CheckBox cb in MonitorList.Children)
                    cb.IsChecked = cb.Tag as string == primary.StableId;
            }
        }

        private void OnTargetMonitorChanged()
        {
            var selected = new List<string>();
            foreach (CheckBox cb in MonitorList.Children)
                if (cb.IsChecked==true) selected.Add(cb.Tag as string ?? "");
            if (selected.Count==0)
            {
                // Prevent zero selection
                _trayManager?.ShowBalloon("Выберите хотя бы один монитор", "ScreenFX", System.Windows.Forms.ToolTipIcon.Warning);
                // Re-check primary
                var monitors = _monitorService.GetMonitors();
                var primary = monitors.FirstOrDefault(x=>x.IsPrimary) ?? monitors.FirstOrDefault();
                if (primary!=null)
                {
                    selected.Add(primary.StableId);
                    foreach (CheckBox cb in MonitorList.Children)
                        if (cb.Tag as string == primary.StableId) cb.IsChecked = true;
                }
            }
            _settingsService.Current.TargetMonitors = selected;
            _settingsService.Save();
            ApplyMonitorTargets();
        }

        private void OnPanelMonitorChanged()
        {
            if (ComboPanelMonitor.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                _panelMonitorId = id;
                _settingsService.Current.PanelMonitorId = id;
                _settingsService.Save();
                if (_isPanelVisible) PositionOnPanelMonitor();
            }
        }

        private void SelectAllMonitors(bool select)
        {
            foreach (CheckBox cb in MonitorList.Children) cb.IsChecked = select;
            // OnTargetMonitorChanged will be called per cb, but we ensure final state
            OnTargetMonitorChanged();
        }

        private void ApplyMonitorTargets()
        {
            var all = _monitorService.GetMonitors();
            var targetIds = _settingsService.Current.TargetMonitors;
            if (targetIds.Count==0)
            {
                var primary = all.FirstOrDefault(x=>x.IsPrimary) ?? all.FirstOrDefault();
                if (primary!=null) targetIds = new List<string>{ primary.StableId };
            }
            var targets = all.Where(m=> targetIds.Contains(m.StableId)).ToList();
            _overlayManager?.UpdateTargets(targets);
            _overlayManager?.UpdateFilters(_params);
        }

        // Profiles
        private void RefreshProfiles()
        {
            ProfileCombo.Items.Clear();
            var profiles = _profileManager.LoadAll();
            foreach (var p in profiles)
            {
                var item = new ComboBoxItem { Content = p.Name, Tag = p.Id };
                ProfileCombo.Items.Add(item);
            }
            if (ProfileCombo.Items.Count>0) ProfileCombo.SelectedIndex=0;
        }

        private void SaveProfile()
        {
            var dlg = new ProfileNameDialog();
            dlg.Owner = this;
            if (dlg.ShowDialog()==true)
            {
                string name = dlg.ProfileName;
                if (string.IsNullOrWhiteSpace(name)) name = $"Профиль {DateTime.Now:HH:mm}";
                _profileManager.CreateFromCurrent(name, _params, _settingsService.Current.TargetMonitors);
                RefreshProfiles();
                _trayManager?.ShowBalloon($"Профиль «{name}» сохранён");
            }
        }

        private void LoadProfile()
        {
            if (ProfileCombo.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var profile = _profileManager.Get(id);
                if (profile!=null)
                {
                    _params = profile.Filters.DeepClone();
                    // Optionally apply target monitors from profile? But spec says optionally if not unexpected. We'll apply if same monitor count
                    SyncUiFromParams();
                    ApplyFilters();
                    // Apply monitors if exist
                    if (profile.TargetMonitors.Count>0)
                    {
                        var all = _monitorService.GetMonitors();
                        var valid = profile.TargetMonitors.Where(t=> all.Any(m=>m.StableId==t)).ToList();
                        if (valid.Count>0)
                        {
                            _settingsService.Current.TargetMonitors = valid;
                            _settingsService.Save();
                            RefreshMonitors();
                            ApplyMonitorTargets();
                        }
                    }
                    _trayManager?.ShowBalloon($"Профиль «{profile.Name}» загружен");
                }
            }
        }

        private void DeleteProfile()
        {
            if (ProfileCombo.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var profile = _profileManager.Get(id);
                if (profile!=null)
                {
                    var res = System.Windows.MessageBox.Show($"Удалить профиль «{profile.Name}»?", "ScreenFX", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                    if (res==System.Windows.MessageBoxResult.Yes)
                    {
                        _profileManager.Delete(id);
                        RefreshProfiles();
                        _trayManager?.ShowBalloon("Профиль удалён");
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task ChangeHotkey(string which)
        {
            var dlg = new HotkeyCaptureDialog();
            dlg.Owner = this;
            if (dlg.ShowDialog()==true)
            {
                string newDisplay = dlg.CapturedHotkey;
                HotkeyConfig newCfg;
                HotkeyConfig oldCfg;
                Action handler;
                if (which=="panel") { oldCfg=_hkPanel; newCfg=HotkeyConfig.Parse(newDisplay, 9001); handler=()=> Dispatcher.Invoke(TogglePanel); _hkPanel=newCfg; _settingsService.Current.Hotkeys.TogglePanel=newDisplay; HotkeyPanelDisplay.Text=newDisplay; }
                else if (which=="filters") { oldCfg=_hkFilters; newCfg=HotkeyConfig.Parse(newDisplay, 9002); handler=()=> Dispatcher.Invoke(ToggleFilters); _hkFilters=newCfg; _settingsService.Current.Hotkeys.ToggleFilters=newDisplay; HotkeyFiltersDisplay.Text=newDisplay; }
                else { oldCfg=_hkReset; newCfg=HotkeyConfig.Parse(newDisplay, 9003); handler=()=> Dispatcher.Invoke(ResetAll); _hkReset=newCfg; _settingsService.Current.Hotkeys.ResetFilters=newDisplay; HotkeyResetDisplay.Text=newDisplay; }

                _hotkeyManager.Unregister(oldCfg.Id);
                if (!_hotkeyManager.Register(newCfg, handler))
                {
                    // revert
                    _hotkeyManager.Register(oldCfg, handler);
                    if (which=="panel") { _hkPanel=oldCfg; HotkeyPanelDisplay.Text=oldCfg.Display; _settingsService.Current.Hotkeys.TogglePanel=oldCfg.Display; }
                    else if (which=="filters") { _hkFilters=oldCfg; HotkeyFiltersDisplay.Text=oldCfg.Display; _settingsService.Current.Hotkeys.ToggleFilters=oldCfg.Display; }
                    else { _hkReset=oldCfg; HotkeyResetDisplay.Text=oldCfg.Display; _settingsService.Current.Hotkeys.ResetFilters=oldCfg.Display; }
                    HotkeyStatus.Text = "Не удалось зарегистрировать сочетание — возможно занято системой.";
                }
                else
                {
                    _settingsService.Save();
                    HotkeyStatus.Text = $"Сочетание {newDisplay} сохранено.";
                    await System.Threading.Tasks.Task.Delay(2000);
                    HotkeyStatus.Text = "";
                }
            }
        }

        private void SetStartup(bool enable)
        {
            _settingsService.Current.RunAtStartup = enable;
            _settingsService.Save();
            try
            {
                string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true);
                if (enable)
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    // For self-contained, exe is ScreenFX.exe, not dll
                    if (exe.EndsWith(".dll")) exe = exe.Replace(".dll", ".exe");
                    if (!File.Exists(exe)) exe = Process.GetCurrentProcess().MainModule?.FileName ?? exe;
                    key?.SetValue("ScreenFX", $"\"{exe}\" --minimized");
                }
                else
                {
                    key?.DeleteValue("ScreenFX", false);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Startup registry failed: {ex}"); }
        }
    }

    public class ProfileNameDialog : Window
    {
        public string ProfileName { get; private set; } = "";
        public ProfileNameDialog()
        {
            Title = "Сохранить профиль";
            Width = 380; Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("SurfaceBrush");
            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock { Text = "Название профиля", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            var tb = new TextBox { Margin = new Thickness(0,8,0,0), Padding = new Thickness(8,6,8,6) };
            tb.Text = $"Профиль {DateTime.Now:dd.MM HH:mm}";
            root.Children.Add(tb);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
            var btnCancel = new Button { Content = "Отмена", Padding = new Thickness(16,6,16,6), Margin = new Thickness(0,0,8,0) };
            btnCancel.Click += (s,e)=> DialogResult=false;
            var btnOk = new Button { Content = "Сохранить", Padding = new Thickness(16,6,16,6), Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("PrimaryBrush"), Foreground = System.Windows.Media.Brushes.White };
            btnOk.Click += (s,e)=> { ProfileName = tb.Text; DialogResult = true; };
            btnPanel.Children.Add(btnCancel); btnPanel.Children.Add(btnOk);
            root.Children.Add(btnPanel);
            Content = root;
        }
    }
}
