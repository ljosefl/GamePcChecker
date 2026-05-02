using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GamePcChecker.App.StressTest;

/// <summary>
/// Непрерывная отрисовка D3D11: полноэкранный треугольник + «тяжёлый» pixel shader (не бенчмарк игры).
/// </summary>
internal sealed class DirectXStressRenderer : IDisposable
{
    private readonly IntPtr _hwnd;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _rtv;
    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11Buffer? _cbIntensity;

    private int _width;
    private int _height;
    private bool _vsync = true;
    private uint _intensityIterations = 4096;

    public DirectXStressRenderer(IntPtr hwnd, int initialWidth, int initialHeight)
    {
        _hwnd = hwnd;
        _width = Math.Max(64, initialWidth);
        _height = Math.Max(64, initialHeight);
        InitDeviceAndSwapChain();
        CompileShaders();
        CreateIntensityBuffer();
        if (_device != null && _swapChain != null && _vs != null && _ps != null)
            ResizeBuffers(_width, _height);
    }

    public bool IsReady => _device != null && _swapChain != null && _vs != null && _ps != null && _rtv != null;

    public string? LastError { get; private set; }

    public bool Vsync
    {
        get => _vsync;
        set => _vsync = value;
    }

    /// <summary>Грубая «тяжесть» шейдера: базовые итерации цикла в PS (1–200 → шкала умножения).</summary>
    public void SetIntensityPercent(int percent)
    {
        percent = Math.Clamp(percent, 1, 200);
        _intensityIterations = (uint)(percent * 64);
        UploadIntensity();
    }

    private void InitDeviceAndSwapChain()
    {
        try
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

            var hr = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out var device,
                out _,
                out var ctx);

            if (hr.Failure || device == null || ctx == null)
            {
                LastError = $"D3D11CreateDevice: {hr}";
                return;
            }

            _device = device;
            _context = ctx;

            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
            if (_factory == null)
            {
                LastError = "CreateDXGIFactory2 не удался.";
                return;
            }

            var desc = new SwapChainDescription1(
                (uint)_width,
                (uint)_height,
                Format.B8G8R8A8_UNorm,
                stereo: false,
                Usage.RenderTargetOutput,
                bufferCount: 2,
                Scaling.Stretch,
                SwapEffect.FlipDiscard,
                AlphaMode.Ignore,
                SwapChainFlags.AllowModeSwitch)
            {
                SampleDescription = new SampleDescription(1, 0),
            };

            // Vortice 3.x: swap chain возвращается методом, без out-параметра.
            _swapChain = _factory.CreateSwapChainForHwnd(device, _hwnd, desc, null, null);

            if (_swapChain == null)
            {
                LastError = "CreateSwapChainForHwnd не вернул swap chain.";
                return;
            }

            _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void CompileShaders()
    {
        if (_device == null)
            return;

        const string hlsl = """
                            struct PSIn { float4 pos : SV_Position; };

                            cbuffer Params : register(b0) {
                              uint g_iters;
                              uint _pad0;
                              uint _pad1;
                              uint _pad2;
                            };

                            float4 VS(uint vid : SV_VertexID) : SV_Position {
                              float2 uv = float2((vid << 1) & 2, vid & 2);
                              return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                            }

                            float4 PS(PSIn pin) : SV_Target {
                              float2 p = pin.pos.xy;
                              float4 acc = float4(0, 0, 0, 0);
                              uint n = max(g_iters, 256u);
                              for (uint i = 0u; i < n; i++) {
                                float t = (float)i * 0.00013 + p.x * 0.001 + p.y * 0.001;
                                acc += float4(sin(t), cos(t * 1.3), sin(t * 0.7 + p.y * 0.002), cos(t * 0.5));
                              }
                              return frac(acc * 0.001);
                            }
                            """;

        const string sourceName = "GamePcCheckerStress.hlsl";
        var hrVs = Compiler.Compile(hlsl, "VS", sourceName, "vs_5_0", out var vsBlob, out var vsErr);
        using (vsErr)
        using (vsBlob)
        {
            if (hrVs.Failure || vsBlob == null)
            {
                LastError = GetBlobUtf8(vsErr) ?? $"VS compile: {hrVs}";
                return;
            }

            var hrPs = Compiler.Compile(hlsl, "PS", sourceName, "ps_5_0", out var psBlob, out var psErr);
            using (psErr)
            using (psBlob)
            {
                if (hrPs.Failure || psBlob == null)
                {
                    LastError = GetBlobUtf8(psErr) ?? $"PS compile: {hrPs}";
                    return;
                }

                _vs = _device.CreateVertexShader(vsBlob);
                _ps = _device.CreatePixelShader(psBlob);
            }
        }
    }

    private static string? GetBlobUtf8(Blob? b)
    {
        if (b == null)
            return null;
        if (b.BufferPointer == IntPtr.Zero)
            return null;
        // Сообщения D3DCompile — восьмибитные ANSI, не UTF-8.
        return Marshal.PtrToStringAnsi(b.BufferPointer);
    }

    private void CreateIntensityBuffer()
    {
        if (_device == null)
            return;

        _cbIntensity = _device.CreateBuffer(
            new BufferDescription(
                16,
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0));

        UploadIntensity();
    }

    private void UploadIntensity()
    {
        if (_context == null || _cbIntensity == null)
            return;

        var data = new IntensityCb { It = _intensityIterations };
        var h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            _context.UpdateSubresource(_cbIntensity, 0, null, h.AddrOfPinnedObject(), 16, 0);
        }
        finally
        {
            h.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntensityCb
    {
        public uint It;
        public uint P0;
        public uint P1;
        public uint P2;
    }

    public void Resize(int w, int h)
    {
        w = Math.Max(64, w);
        h = Math.Max(64, h);
        if (w == _width && h == _height)
            return;

        _width = w;
        _height = h;
        ResizeBuffers(w, h);
    }

    private void ResizeBuffers(int w, int h)
    {
        if (_swapChain == null || _device == null || _context == null)
            return;

        _rtv?.Dispose();
        _rtv = null;

        _swapChain.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown, SwapChainFlags.None);

        using var bb = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(bb);
    }

    public void RenderFrame()
    {
        if (_context == null || _swapChain == null || _rtv == null || _vs == null || _ps == null || _cbIntensity == null)
            return;

        var vp = new Viewport(0, 0, _width, _height, 0, 1);
        _context.RSSetViewport(vp);
        _context.OMSetRenderTargets(_rtv);
        _context.ClearRenderTargetView(_rtv, new Color4(0.06f, 0.07f, 0.09f, 1f));

        _context.VSSetShader(_vs, null, 0);
        _context.PSSetShader(_ps, null, 0);
        _context.PSSetConstantBuffer(0, _cbIntensity);

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.Draw(3, 0);

        var interval = _vsync ? 1u : 0u;
        _swapChain.Present(interval, PresentFlags.None);
    }

    public void Dispose()
    {
        _rtv?.Dispose();
        _vs?.Dispose();
        _ps?.Dispose();
        _cbIntensity?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}
