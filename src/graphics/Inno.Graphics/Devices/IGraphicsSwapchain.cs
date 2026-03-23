using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Represents a presentable output chain.
/// </summary>
public interface IGraphicsSwapchain : IGraphicsResource
{
    int width { get; }

    int height { get; }

    PixelFormat colorFormat { get; }

    PixelFormat depthFormat { get; }

    void Resize(int width, int height);

    void Present();
}
