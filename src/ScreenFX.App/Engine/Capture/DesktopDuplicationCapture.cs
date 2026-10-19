using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ScreenFX.Monitors;

namespace ScreenFX.Engine.Capture
{
    public sealed class DesktopDuplicationCapture : IDisposable
    {
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _currentTexture;
        private IDXGIOutput1? _output1;
        private readonly MonitorInfo _monitor;
        private bool _disposed;
        private int _width;
        private int _height;

        public MonitorInfo Monitor => _monitor;
        public int Width => _width;
        public int Height => _height;
        public bool IsInitialized { get; private set; }

        public DesktopDuplicationCapture(MonitorInfo monitor)
        {
            _monitor = monitor;
        }

        public bool Initialize(ID3D11Device device, ID3D11DeviceContext context)
        {
            try
            {
                _device = device;
                _context = context;

                using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();

                for (int i = 0; ; i++)
                {
                    var hrEnum = adapter.EnumOutputs(i, out IDXGIOutput? output);
                    if (hrEnum.Failure || output == null) break;
                    try
                    {
                        var desc = output.Description;
                        bool matchCoords = desc.DesktopCoordinates.Left == _monitor.Left &&
                                           desc.DesktopCoordinates.Top == _monitor.Top &&
                                           desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left == _monitor.Width &&
                                           desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top == _monitor.Height;
                        bool matchName = desc.DeviceName == _monitor.DeviceName;
                        if (matchCoords || matchName)
                        {
                            _output1 = output.QueryInterface<IDXGIOutput1>();
                            output.Dispose();
                            break;
                        }
                    }
                    finally
                    {
                        // output disposed in branches above or here
                    }
                    output.Dispose();
                }

                if (_output1 == null)
                {
                    using var adapter2 = dxgiDevice.GetAdapter();
                    var hr0 = adapter2.EnumOutputs(0, out IDXGIOutput? out0);
                    if (hr0.Success && out0 != null)
                    {
                        _output1 = out0.QueryInterface<IDXGIOutput1>();
                        out0.Dispose();
                    }
                }

                if (_output1 == null) return false;

                _duplication = _output1.DuplicateOutput(device);
                var outDesc = _output1.Description;
                _width = outDesc.DesktopCoordinates.Right - outDesc.DesktopCoordinates.Left;
                _height = outDesc.DesktopCoordinates.Bottom - outDesc.DesktopCoordinates.Top;
                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DesktopDuplication Initialize failed for {_monitor.DeviceName}: {ex.Message}");
                IsInitialized = false;
                return false;
            }
        }

        public ID3D11Texture2D? AcquireNextFrame(int timeoutMs = 16)
        {
            if (_disposed || _duplication == null) return null;
            try
            {
                var hr = _duplication.AcquireNextFrame(timeoutMs, out OutduplFrameInfo frameInfo, out IDXGIResource? desktopResource);
                if (hr.Failure)
                {
                    if (hr.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) return null;
                    if (hr.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
                        throw new InvalidOperationException("DXGI_ERROR_ACCESS_LOST");
                    return null;
                }
                if (desktopResource == null) return null;
                using (desktopResource)
                {
                    var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    _currentTexture = texture;
                    return texture;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("ACCESS_LOST"))
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AcquireNextFrame error: {ex.Message}");
                return null;
            }
        }

        public void ReleaseFrame()
        {
            if (_currentTexture != null)
            {
                _currentTexture.Dispose();
                _currentTexture = null;
            }
            try { _duplication?.ReleaseFrame(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseFrame();
            _duplication?.Dispose();
            _output1?.Dispose();
        }
    }

    public class CaptureManager : IDisposable
    {
        private readonly Dictionary<string, DesktopDuplicationCapture> _captures = new();
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;

        public bool InitializeDevice()
        {
            try
            {
                var flags = DeviceCreationFlags.BgraSupport;
                Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                    null, DriverType.Hardware, flags,
                    new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                    out ID3D11Device? device, out FeatureLevel _, out ID3D11DeviceContext? context);
                if (device == null || context == null)
                {
                    Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                        null, DriverType.Warp, flags,
                        new[] { FeatureLevel.Level_11_0 },
                        out device, out _, out context);
                }
                _device = device;
                _context = context;
                return device != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create D3D11 device: {ex}");
                return false;
            }
        }

        public ID3D11Device? Device => _device;
        public ID3D11DeviceContext? Context => _context;

        public DesktopDuplicationCapture? GetOrCreate(MonitorInfo monitor)
        {
            string key = monitor.StableId;
            if (_captures.TryGetValue(key, out var existing)) return existing;
            if (_device == null || _context == null) return null;
            var cap = new DesktopDuplicationCapture(monitor);
            if (cap.Initialize(_device, _context))
            {
                _captures[key] = cap;
                return cap;
            }
            cap.Dispose();
            return null;
        }

        public void Remove(string stableId)
        {
            if (_captures.TryGetValue(stableId, out var c))
            {
                c.Dispose();
                _captures.Remove(stableId);
            }
        }

        public void Dispose()
        {
            foreach (var c in _captures.Values) c.Dispose();
            _captures.Clear();
            _context?.Dispose();
            _device?.Dispose();
        }
    }
}
