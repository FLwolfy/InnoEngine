using Inno.Graphics;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxSwapchain : DisposableGraphicsResource, IGraphicsSwapchain
{
    public BgfxSwapchain(GraphicsSwapchainDescription description)
    {
        width = description.width;
        height = description.height;
        colorFormat = description.colorFormat;
        depthFormat = description.depthFormat;
    }

    public int width { get; private set; }

    public int height { get; private set; }

    public PixelFormat colorFormat { get; }

    public PixelFormat depthFormat { get; }

    public void Resize(int width, int height)
    {
        this.width = width;
        this.height = height;
    }

    public void Present()
    {
        throw new NotImplementedException("bgfx present is not implemented yet.");
    }
}
