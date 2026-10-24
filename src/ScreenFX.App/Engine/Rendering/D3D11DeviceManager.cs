using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;

namespace ScreenFX.Engine.Rendering
{
    public sealed class D3D11DeviceManager : IDisposable
    {
        public ID3D11Device? Device { get; private set; }
        public ID3D11DeviceContext? Context { get; private set; }
        public IDXGIFactory2? Factory { get; private set; }
        public bool IsWARP { get; private set; }

        public bool Initialize()
        {
            try
            {
                var flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
                // flags |= DeviceCreationFlags.Debug;
#endif
                var hr = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                    null, DriverType.Hardware, flags,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                    out ID3D11Device? dev, out FeatureLevel level, out ID3D11DeviceContext? ctx);
                if (hr.Failure || dev == null)
                {
                    hr = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                        null, DriverType.Warp, flags,
                        new[] { FeatureLevel.Level_11_0 },
                        out dev, out level, out ctx);
                    IsWARP = true;
                }
                Device = dev;
                Context = ctx;
                if (dev != null)
                {
                    using var dxgiDev = dev.QueryInterface<IDXGIDevice>();
                    using var adapter = dxgiDev.GetAdapter();
                    Factory = adapter.GetParent<IDXGIFactory2>();
                }
                return Device != null && Context != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"D3D11 init failed: {ex}");
                return false;
            }
        }

        public System.ReadOnlyMemory<byte> CompileShader(string hlsl, string entry, string profile, string sourceName = "Shader")
        {
            return Compiler.Compile(hlsl, entry, sourceName, profile, Vortice.D3DCompiler.ShaderFlags.None, Vortice.D3DCompiler.EffectFlags.None);
        }

        public void Dispose()
        {
            Context?.Dispose();
            Device?.Dispose();
            Factory?.Dispose();
        }
    }
}
