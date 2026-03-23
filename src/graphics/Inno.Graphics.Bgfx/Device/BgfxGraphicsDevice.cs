using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxGraphicsDevice : IGraphicsDevice
{
    private bool m_initialized;
    private bool m_disposed;
    private BgfxSwapchain? m_swapchain;
    private bgfx.RendererType m_rendererType = bgfx.RendererType.Count;

    public GraphicsBackendKind backendKind => GraphicsBackendKind.Bgfx;

    public GraphicsLimits limits { get; } = new();

    public bgfx.RendererType rendererType => m_rendererType;

    public IGraphicsSwapchain CreateSwapchain(GraphicsSwapchainDescription description)
    {
        m_swapchain = new BgfxSwapchain(description, this);
        EnsureInitialized(m_swapchain);
        return m_swapchain;
    }

    public IGraphicsBuffer CreateBuffer(BufferDescription description) => new BgfxBuffer(description);

    public IGraphicsTexture CreateTexture(TextureDescription description) => new BgfxTexture(description);

    public IGraphicsSampler CreateSampler(SamplerDescription description) => new BgfxSampler();

    public IGraphicsShader CreateShader(ShaderDescription description) => new BgfxShader(description);

    public IGraphicsProgram CreateProgram(GraphicsProgramDescription description) => new BgfxProgram(description);

    public IGraphicsInputLayout CreateInputLayout(GraphicsInputLayoutDescription description) => new BgfxInputLayout(description);

    public IGraphicsRenderPipeline CreateRenderPipeline(GraphicsRenderPipelineDescription description) => new BgfxRenderPipeline(description);

    public IGraphicsRenderTarget CreateRenderTarget(GraphicsRenderTargetDescription description) => new BgfxRenderTarget(description);

    public IGraphicsResourceSet CreateResourceSet(ResourceSetDescription description) => new BgfxResourceSet();

    public IGraphicsCommandList CreateCommandList() => new BgfxCommandList();

    public IGraphicsContext CreateContext() => new BgfxGraphicsContext(this);

    public void Submit(IGraphicsCommandList commandList)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(commandList);

        if (commandList is not BgfxCommandList bgfxCommandList)
        {
            throw new ArgumentException("Command list must be BgfxCommandList.", nameof(commandList));
        }

        if (!m_initialized)
        {
            throw new InvalidOperationException("Swapchain must be created before submitting commands.");
        }

        if (bgfxCommandList.isRecording)
        {
            throw new InvalidOperationException("Command list must be ended before submission.");
        }
    }

    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_initialized)
        {
            bgfx.frame(0);
        }
    }

    public void WaitIdle()
    {
        if (m_initialized)
        {
            bgfx.frame(0);
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        if (m_initialized)
        {
            bgfx.shutdown();
            m_initialized = false;
        }

        m_disposed = true;
    }

    internal void ResetBackbuffer(int width, int height, bool vSync, PixelFormat colorFormat)
    {
        if (!m_initialized)
        {
            return;
        }

        var flags = vSync ? bgfx.ResetFlags.Vsync : bgfx.ResetFlags.None;
        bgfx.reset((uint)width, (uint)height, (uint)flags, BgfxFormatConverter.ToBgfxTextureFormat(colorFormat));
    }

    private unsafe void EnsureInitialized(BgfxSwapchain swapchain)
    {
        if (m_initialized)
        {
            return;
        }

        if (swapchain.nativeHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Swapchain nativeHandle cannot be zero.", nameof(swapchain));
        }

        bgfx.Init init = default;
        bgfx.init_ctor(&init);
        
        bgfx.PlatformData platformData = default;
        platformData.nwh = (void*)swapchain.nativeHandle;
        platformData.ndt = (void*)swapchain.nativeDisplayHandle;
        platformData.type = swapchain.nativeWindowKind switch
        {
            GraphicsNativeWindowKind.Wayland => bgfx.NativeWindowHandleType.Wayland,
            _ => bgfx.NativeWindowHandleType.Default
        };

        init.platformData = platformData;
        bgfx.set_platform_data(&platformData);
        init.type = OperatingSystem.IsMacOS()
            ? bgfx.RendererType.Metal
            : bgfx.RendererType.Count;
        init.resolution.width = (uint)swapchain.width;
        init.resolution.height = (uint)swapchain.height;
        init.resolution.reset = (uint)(swapchain.vSync ? bgfx.ResetFlags.Vsync : bgfx.ResetFlags.None);
        init.resolution.formatColor = BgfxFormatConverter.ToBgfxTextureFormat(swapchain.colorFormat);
        if (!bgfx.init(&init))
        {
            throw new InvalidOperationException("bgfx init failed.");
        }

        m_rendererType = bgfx.get_renderer_type();
        m_initialized = true;
    }
}
