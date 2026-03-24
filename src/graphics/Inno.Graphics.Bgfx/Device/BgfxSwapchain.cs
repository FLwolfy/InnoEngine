using Inno.Graphics;

using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxSwapchain : DisposableGraphicsResource, IGraphicsSwapchain
{
    private readonly BgfxGraphicsDevice m_device;

    public BgfxSwapchain(GraphicsSwapchainDescription description, BgfxGraphicsDevice device)
    {
        m_device = device;
        nativeHandle = description.nativeHandle;
        nativeDisplayHandle = description.nativeDisplayHandle;
        nativeWindowKind = description.nativeWindowKind;
        width = description.width;
        height = description.height;
        colorFormat = description.colorFormat;
        depthFormat = description.depthFormat;
        vSync = description.vSync;
    }

    public IntPtr nativeHandle { get; }

    public IntPtr nativeDisplayHandle { get; }

    public GraphicsNativeWindowKind nativeWindowKind { get; }

    public int width { get; private set; }

    public int height { get; private set; }

    public PixelFormat colorFormat { get; }

    public PixelFormat depthFormat { get; }

    public bool vSync { get; }

    public void Resize(int width, int height)
    {
        this.width = width;
        this.height = height;
        m_device.ResetBackbuffer(width, height, vSync, colorFormat);
    }

    public void Present()
    {
        bgfx.frame(0);
    }
}
