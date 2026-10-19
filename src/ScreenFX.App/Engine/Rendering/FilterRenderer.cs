using System;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Mathematics;
using ScreenFX.Engine.Filters;

namespace ScreenFX.Engine.Rendering
{
    public sealed class FilterRenderer : IDisposable
    {
        private readonly D3D11DeviceManager _deviceManager;
        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _ps;
        private ID3D11InputLayout? _layout; // not needed for fullscreen triangle but keep
        private ID3D11Buffer? _constantBuffer;
        private ID3D11SamplerState? _sampler;
        private ID3D11Texture2D? _intermediateTexture; // for multi-pass if needed
        private bool _initialized;

        public FilterRenderer(D3D11DeviceManager deviceManager)
        {
            _deviceManager = deviceManager;
        }

        public bool Initialize()
        {
            try
            {
                var device = _deviceManager.Device!;
                // Load shaders from embedded resources or files
                string shaderDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine", "Rendering", "Shaders");
                // Fallback to executable dir
                if (!Directory.Exists(shaderDir))
                    shaderDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders");
                // If still not found, use embedded HLSL strings
                string vsHlsl = GetEmbeddedVS();
                string psHlsl = GetEmbeddedPS();

                // Try file first
                string vsPath = Path.Combine(shaderDir, "FilterVS.hlsl");
                string psPath = Path.Combine(shaderDir, "FilterPS.hlsl");
                if (File.Exists(vsPath)) vsHlsl = File.ReadAllText(vsPath);
                if (File.Exists(psPath)) psHlsl = File.ReadAllText(psPath);

                var vsBytecode = Compiler.Compile(vsHlsl, "main", "FilterVS", "vs_5_0");
                _vs = device.CreateVertexShader(vsBytecode.Span);

                var psBytecode = Compiler.Compile(psHlsl, "main", "FilterPS", "ps_5_0");
                _ps = device.CreatePixelShader(psBytecode.Span);

                // Constant buffer: size must be multiple of 16
                int cbSize = Marshal.SizeOf<FilterParamsGpu>();
                cbSize = (cbSize + 15) & ~15;
                var cbDesc = new BufferDescription(cbSize, BindFlags.ConstantBuffer, ResourceOptionFlags.None, ResourceUsage.Dynamic, CpuAccessFlags.Write, 0);
                _constantBuffer = device.CreateBuffer(cbDesc);

                var sampDesc = new SamplerDescription
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    ComparisonFunction = ComparisonFunction.Never,
                    MinLOD = 0,
                    MaxLOD = float.MaxValue
                };
                _sampler = device.CreateSamplerState(sampDesc);

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FilterRenderer init failed: {ex}");
                return false;
            }
        }

        public void UpdateConstants(FilterParamsGpu gpuParams)
        {
            if (!_initialized || _constantBuffer == null) return;
            var ctx = _deviceManager.Context!;
            var mapped = ctx.Map(_constantBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
            Marshal.StructureToPtr(gpuParams, mapped.DataPointer, false);
            ctx.Unmap(_constantBuffer, 0);
        }

        public void Render(ID3D11ShaderResourceView srcSrv, ID3D11RenderTargetView rtv, Viewport viewport)
        {
            if (!_initialized) return;
            var ctx = _deviceManager.Context!;
            ctx.RasterizerStage.SetViewport(viewport);
            ctx.OutputMerger.SetRenderTargets(rtv);
            ctx.VertexShader.Set(_vs);
            ctx.PixelShader.Set(_ps);
            ctx.PixelShader.SetConstantBuffer(0, _constantBuffer);
            ctx.PixelShader.SetShaderResource(0, srcSrv);
            ctx.PixelShader.SetSampler(0, _sampler);
            // No vertex buffer, draw fullscreen triangle
            ctx.InputAssembler.PrimitiveTopology = Vortice.Direct3D.PrimitiveTopology.TriangleList;
            ctx.Draw(3, 0);
            // Unbind
            ctx.PixelShader.SetShaderResource(0, null);
        }

        private string GetEmbeddedVS() => @"
struct VSOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
VSOutput main(uint vertexID : SV_VertexID) {
    VSOutput o; float2 uv = float2((vertexID << 1) & 2, vertexID & 2); o.uv = uv;
    o.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0); return o; }";

        private string GetEmbeddedPS()
        {
            // For fallback, try to load from resource file embedded at build
            try
            {
                var asm = typeof(FilterRenderer).Assembly;
                using var s = asm.GetManifestResourceStream("ScreenFX.Engine.Rendering.Shaders.FilterPS.hlsl");
                if (s != null) using (var r = new StreamReader(s)) return r.ReadToEnd();
            } catch {}
            // Minimal passthrough if full shader missing
            return @"
Texture2D screenTexture : register(t0); SamplerState screenSampler : register(s0);
cbuffer FilterParams : register(b0) { float saturation; float exposure; float hueShift; float temperature; float nightModeStrength; int enableNegative; int enableHalftone; float halftoneStrength; float halftoneDotSize; float halftoneContrast; float halftoneAngle; float sharpenStrength; float blurStrength; float circularBlurRadius; float circularBlurStrength; float circularBlurSmoothness; float2 circularBlurCenter; float2 resolution; float hsvHue; float hsvSaturation; float hsvValue; int letterboxEnabled; float letterboxHeight; float letterboxAspect; float3 letterboxColor; float _pad0; float2 _pad1; };
float3 rgb2hsv(float3 c){ float4 K=float4(0.0,-1.0/3.0,2.0/3.0,-1.0); float4 p=lerp(float4(c.bg,K.wz),float4(c.gb,K.xy),step(c.b,c.g)); float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzw),step(p.x,c.r)); float d=q.x-min(q.w,q.y); float e=1e-10; return float3(abs(q.z+(q.w-q.y)/(6*d+e)),d/(q.x+e),q.x); }
float3 hsv2rgb(float3 c){ float4 K=float4(1.0,2.0/3.0,1.0/3.0,3.0); float3 p=abs(frac(c.xxx+K.xyz)*6.0-K.www); return c.z*lerp(K.xxx,clamp(p-K.xxx,0,1),c.y); }
float luminance(float3 c){return dot(c,float3(0.2126,0.7152,0.0722));}
float4 main(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target{
    if(letterboxEnabled!=0 && (uv.y<letterboxHeight || uv.y>1-letterboxHeight)) return float4(letterboxColor,1);
    float3 col=screenTexture.Sample(screenSampler,uv).rgb;
    col*=exp2(exposure); col=saturate(col);
    if(temperature!=0){ if(temperature>0) col*=lerp(float3(1,1,1),float3(1.2,1.0,0.8),temperature); else col*=lerp(float3(1,1,1),float3(0.8,0.9,1.2),-temperature); }
    if(nightModeStrength>0) col=lerp(col,float3(col.r*1.05,col.g*0.98,col.b*(1-nightModeStrength*0.6)),nightModeStrength);
    float l=luminance(col); col=lerp(float3(l,l,l),col,saturation);
    if(abs(hueShift)>0.001){ float3 hsv=rgb2hsv(col); hsv.x=frac(hsv.x+hueShift); col=hsv2rgb(hsv); }
    if(abs(hsvHue)>0.001||abs(hsvSaturation-1)>0.001||abs(hsvValue-1)>0.001){ float3 hsv=rgb2hsv(col); hsv.x=frac(hsv.x+hsvHue); hsv.y=saturate(hsv.y*hsvSaturation); hsv.z=saturate(hsv.z*hsvValue); col=hsv2rgb(hsv); }
    if(enableNegative!=0) col=1-col;
    if(sharpenStrength>0.001){ float2 texel=1/resolution; float3 n=screenTexture.Sample(screenSampler,uv+float2(0,-texel.y)).rgb; float3 s=screenTexture.Sample(screenSampler,uv+float2(0,texel.y)).rgb; float3 w=screenTexture.Sample(screenSampler,uv+float2(-texel.x,0)).rgb; float3 e=screenTexture.Sample(screenSampler,uv+float2(texel.x,0)).rgb; float3 cen=col; float3 lap=cen*4-(n+s+w+e); col=saturate(cen+lap*sharpenStrength*0.6); }
    float3 sharp=col; float3 blurred=sharp; bool need=blurStrength>0.001||circularBlurStrength>0.001; if(need){ float2 texel=1/resolution; float3 sum=0; for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++){ float2 off=float2(x,y)*texel*1.5; float wgt=(x==0&&y==0)?0.25:(abs(x)+abs(y)==1?0.125:0.0625); sum+=screenTexture.Sample(screenSampler,uv+off).rgb*wgt; } float3 b=sum*exp2(exposure); if(temperature>0) b*=lerp(float3(1,1,1),float3(1.2,1.0,0.8),temperature); else if(temperature<0) b*=lerp(float3(1,1,1),float3(0.8,0.9,1.2),-temperature); if(nightModeStrength>0) b=lerp(b,float3(b.r*1.05,b.g*0.98,b.b*(1-nightModeStrength*0.6)),nightModeStrength); float lb=luminance(b); b=lerp(float3(lb,lb,lb),b,saturation); if(abs(hueShift)>0.001){ float3 hsv=rgb2hsv(b); hsv.x=frac(hsv.x+hueShift); b=hsv2rgb(hsv);} if(abs(hsvHue)>0.001||abs(hsvSaturation-1)>0.001||abs(hsvValue-1)>0.001){ float3 hsv=rgb2hsv(b); hsv.x=frac(hsv.x+hsvHue); hsv.y*=hsvSaturation; hsv.z*=hsvValue; b=hsv2rgb(saturate(hsv)); } if(enableNegative!=0) b=1-b; blurred=b; }
    float3 final=sharp; if(blurStrength>0.001) final=lerp(sharp,blurred,saturate(blurStrength));
    if(circularBlurStrength>0.001 && circularBlurRadius>0.001){ float2 d=(uv-circularBlurCenter); d.x*=resolution.x/min(resolution.x,resolution.y); d.y*=resolution.y/min(resolution.x,resolution.y); float dist=length(d); float r=circularBlurRadius; float ed=r+circularBlurSmoothness; float feather=smoothstep(r,ed,dist); float3 circ=lerp(sharp,blurred,feather*saturate(circularBlurStrength)); if(blurStrength>0.001) final=lerp(final,circ,feather*circularBlurStrength); else final=circ; }
    col=final; if(enableHalftone!=0 && halftoneStrength>0.001){ float lum=luminance(col); lum=saturate((lum-0.5)*halftoneContrast+0.5); float rad=radians(halftoneAngle); float c=cos(rad); float s=sin(rad); float2 rot=float2(uv.x*c-uv.y*s,uv.x*s+uv.y*c); float2 scaled=rot*resolution/max(halftoneDotSize,1); float2 grid=frac(scaled)-0.5; float dist=length(grid); float dotR=(1-lum)*0.45; float dotP=1-smoothstep(dotR-0.08,dotR+0.08,dist); float pat=lerp(1,dotP,halftoneStrength); col*=pat; }
    return float4(saturate(col),1); }";
        }

        public void Dispose()
        {
            _vs?.Dispose();
            _ps?.Dispose();
            _layout?.Dispose();
            _constantBuffer?.Dispose();
            _sampler?.Dispose();
            _intermediateTexture?.Dispose();
        }
    }
}
