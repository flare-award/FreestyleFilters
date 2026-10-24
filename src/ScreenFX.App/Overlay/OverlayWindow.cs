using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ScreenFX.Monitors;
using ScreenFX.Engine.Filters;
using ScreenFX.Engine.Rendering;
using ScreenFX.Engine.Capture;

namespace ScreenFX.Overlay
{
    /// <summary>
    /// Полноэкранное окно-оверлей для одного монитора.
    /// Выводит отфильтрованный кадр через D3D11 SwapChain.
    /// Исключается из захвата через WDA_EXCLUDEFROMCAPTURE.
    /// </summary>
    public class OverlayWindow : IDisposable
    {
        private const int WDA_EXCLUDEFROMCAPTURE = 0x11;
        private const int WDA_MONITOR = 1;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const int GWL_EXSTYLE = -20;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;

        [DllImport("user32.dll")]
        static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        static extern IntPtr CreateWindowEx(int exStyle, string lpClassName, string lpWindowName, int dwStyle, int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        private static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly MonitorInfo _monitor;
        private readonly D3D11DeviceManager _deviceManager;
        private readonly FilterRenderer _filterRenderer;
        private readonly DesktopDuplicationCapture? _capture;
        // Keep each native window procedure alive for the lifetime of its own window.
        private readonly WndProcDelegate _wndProcDelegate;

        private IntPtr _hwnd = IntPtr.Zero;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _rtv;
        private ID3D11Texture2D? _backBuffer;
        private ID3D11ShaderResourceView? _srv;
        private ID3D11Texture2D? _captureStaging; // not used, direct SRV from capture texture
        private bool _disposed;
        private bool _isClickThrough = true;
        private FilterParams _currentParams = new();

        public MonitorInfo Monitor => _monitor;
        public IntPtr Handle => _hwnd;

        public OverlayWindow(MonitorInfo monitor, D3D11DeviceManager deviceManager, FilterRenderer renderer, DesktopDuplicationCapture? capture)
        {
            _monitor = monitor;
            _deviceManager = deviceManager;
            _filterRenderer = renderer;
            _capture = capture;
            _wndProcDelegate = WndProc;
        }

        public bool Create()
        {
            try
            {
                string className = "ScreenFX_Overlay_" + Guid.NewGuid().ToString("N");
                var hInst = GetModuleHandle(null);
                var wc = new WNDCLASS
                {
                    style = 0,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                    hInstance = hInst,
                    lpszClassName = className
                };
                RegisterClass(ref wc);

                int exStyle = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (_isClickThrough) exStyle |= WS_EX_TRANSPARENT;

                _hwnd = CreateWindowEx(
                    exStyle,
                    className,
                    "ScreenFX Overlay",
                    WS_POPUP | WS_VISIBLE,
                    _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height,
                    IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

                if (_hwnd == IntPtr.Zero) return false;

                // Критически важно: исключить из захвата
                bool affinityOk = SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);
                if (!affinityOk)
                {
                    // WDA_MONITOR renders the entire full-screen overlay as black in capture and
                    // creates a permanent black feedback frame. Fail safely instead.
                    System.Diagnostics.Debug.WriteLine("WDA_EXCLUDEFROMCAPTURE failed. Overlay disabled. Error: " + Marshal.GetLastWin32Error());
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                    return false;
                }

                // Force click-through after HWND creation; don't rely only on CreateWindowEx flags.
                SetClickThrough(true);
                SetWindowPos(_hwnd, HWND_TOPMOST, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

                CreateSwapChain();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Overlay create failed: {ex}");
                return false;
            }
        }

        private void CreateSwapChain()
        {
            var device = _deviceManager.Device!;
            var factory = _deviceManager.Factory!;
            var desc = new SwapChainDescription1
            {
                Width = (uint)_monitor.Width,
                Height = (uint)_monitor.Height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Ignore
            };
            _swapChain = factory.CreateSwapChainForHwnd(device, _hwnd, desc, null, null);
            // Prevent alt+enter fullscreen
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

            _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _rtv = device.CreateRenderTargetView(_backBuffer);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_NCHITTEST = 0x84;
            const int HTTRANSPARENT = -1;
            if (msg == WM_NCHITTEST && _isClickThrough)
                return (IntPtr)HTTRANSPARENT;
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void SetClickThrough(bool clickThrough)
        {
            _isClickThrough = clickThrough;
            if (_hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (clickThrough)
                ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            else
                ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

            // SWP_FRAMECHANGED makes Windows re-evaluate the extended styles immediately.
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        public void UpdateFilters(FilterParams p)
        {
            _currentParams = p.DeepClone();
        }

        public void RenderFrame()
        {
            if (_disposed || _hwnd == IntPtr.Zero || _swapChain == null || _rtv == null) return;
            if (_capture == null || !_capture.IsInitialized) return;

            // Если фильтры отключены, приостанавливаем обработку (но не закрываем окно — делаем его прозрачным кликом)
            if (!_currentParams.FiltersEnabled)
            {
                // Можно не рендерить, но нужно чтобы окно не показывало чёрный экран.
                // Делаем окно клик-сквозным и не вызываем Present (сохраняем предыдущий кадр) или презентуем прозрачный?
                // Лучше просто не презентовать и держать окно, но оно перекрывает экран чёрным.
                // Поэтому при отключённых фильтрах мы скрываем оверлей или делаем его прозрачным: заполняем RT прозрачным и презентуем.
                // Проще: если FiltersEnabled==false, то _swapChain.Present с предыдущим кадром всё ещё виден. Поэтому нужно скрыть окно.
                // Мы управляем видимостью из OverlayManager.
                return;
            }

            ID3D11Texture2D? tex = null;
            bool frameAcquired = false;
            try
            {
                tex = _capture.AcquireNextFrame(0);
                if (tex == null) return;
                frameAcquired = true;

                using var srv = _deviceManager.Device!.CreateShaderResourceView(tex);

                var gpuParams = _currentParams.ToGpu(_monitor.Width, _monitor.Height);
                _filterRenderer.UpdateConstants(gpuParams);

                var vp = new Viewport(0, 0, _monitor.Width, _monitor.Height, 0, 1);
                _filterRenderer.Render(srv, _rtv, vp);

                var hr = _swapChain.Present(1, PresentFlags.None);
                if (hr.Failure)
                    System.Diagnostics.Debug.WriteLine($"Present failed: {hr}");
            }
            catch (Exception ex) when (ex.Message.Contains("ACCESS_LOST"))
            {
                // Never leave a stale/black full-screen frame visible after display-mode changes.
                System.Diagnostics.Debug.WriteLine($"Access lost on {_monitor.DeviceName}: {ex}");
                Hide();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render error: {ex}");
            }
            finally
            {
                // AcquireNextFrame and ReleaseFrame must always be paired, including shader/Present errors.
                if (frameAcquired)
                {
                    try { _capture.ReleaseFrame(); } catch { }
                }
                try { tex?.Dispose(); } catch { }
            }
        }

        public void Show()
        {
            if (_hwnd != IntPtr.Zero)
            {
                // Re-assert on every show so toggling filters can never leave an input-blocking window.
                SetClickThrough(true);
                SetWindowPos(_hwnd, HWND_TOPMOST, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        public void Hide()
        {
            if (_hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, HWND_TOPMOST, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height, 0x0080); // SWP_HIDEWINDOW
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _rtv?.Dispose(); } catch {}
            try { _backBuffer?.Dispose(); } catch {}
            try { _swapChain?.Dispose(); } catch {}
            if (_hwnd != IntPtr.Zero)
            {
                try { DestroyWindow(_hwnd); } catch {}
                _hwnd = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hWnd);
    }

    public class OverlayManager : IDisposable
    {
        private readonly D3D11DeviceManager _deviceManager;
        private readonly FilterRenderer _renderer;
        private readonly CaptureManager _captureManager;
        private readonly Dictionary<string, OverlayWindow> _overlays = new();
        private FilterParams _globalParams = new();
        private System.Windows.Threading.DispatcherTimer? _renderTimer;

        public OverlayManager(D3D11DeviceManager dm, FilterRenderer renderer, CaptureManager capMgr)
        {
            _deviceManager = dm;
            _renderer = renderer;
            _captureManager = capMgr;
        }

        public void StartRenderLoop()
        {
            _renderTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _renderTimer.Tick += (s, e) => RenderAll();
            _renderTimer.Start();
        }

        public void StopRenderLoop()
        {
            _renderTimer?.Stop();
            _renderTimer = null;
        }

        private void RenderAll()
        {
            foreach (var ov in _overlays.Values)
            {
                ov.RenderFrame();
            }
        }

        public void UpdateTargets(IEnumerable<MonitorInfo> targets)
        {
            var desired = new HashSet<string>();
            foreach (var m in targets) desired.Add(m.StableId);

            // Remove old
            var toRemove = new List<string>();
            foreach (var kv in _overlays)
                if (!desired.Contains(kv.Key)) toRemove.Add(kv.Key);
            foreach (var k in toRemove)
            {
                _overlays[k].Dispose();
                _captureManager.Remove(k);
                _overlays.Remove(k);
            }

            // Add new
            foreach (var m in targets)
            {
                if (_overlays.ContainsKey(m.StableId)) continue;
                var cap = _captureManager.GetOrCreate(m);
                if (cap == null || !cap.IsInitialized)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture unavailable for {m.DisplayText}; overlay not created");
                    continue;
                }
                var ov = new OverlayWindow(m, _deviceManager, _renderer, cap);
                if (ov.Create())
                {
                    ov.UpdateFilters(_globalParams);
                    // The overlay is visual-only and must never intercept desktop input.
                    ov.SetClickThrough(true);
                    _overlays[m.StableId] = ov;
                }
                else
                {
                    ov.Dispose();
                }
            }
        }

        public void UpdateFilters(FilterParams p)
        {
            _globalParams = p.DeepClone();
            foreach (var ov in _overlays.Values) ov.UpdateFilters(_globalParams);
            if (!_globalParams.FiltersEnabled)
            {
                // Скрываем оверлеи чтобы не блокировать экран чёрным
                foreach (var ov in _overlays.Values) ov.Hide();
                StopRenderLoop();
            }
            else
            {
                foreach (var ov in _overlays.Values) ov.Show();
                if (_renderTimer == null) StartRenderLoop();
            }
        }

        public void SetClickThrough(bool clickThrough)
        {
            foreach (var ov in _overlays.Values) ov.SetClickThrough(clickThrough);
        }

        public void Dispose()
        {
            StopRenderLoop();
            foreach (var ov in _overlays.Values) ov.Dispose();
            _overlays.Clear();
        }
    }
}
