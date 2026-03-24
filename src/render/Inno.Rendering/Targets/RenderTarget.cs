namespace Inno.Rendering;

/// <summary>
/// Represents a high-level render output target.
/// </summary>
public abstract class RenderTarget
{
    public abstract int width { get; }

    public abstract int height { get; }

    public abstract Texture? colorTexture { get; }

    public abstract Texture? depthTexture { get; }

    public static RenderTarget Backbuffer(RenderWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new BackbufferTarget(window);
    }

    public static RenderTarget Texture2D(int width, int height, RenderTargetFormat format)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var descriptor = new RenderTargetDescriptor
        {
            size = new RenderTargetSize(width, height),
            colorFormat = format
        };
        return new TextureRenderTarget(descriptor);
    }
}
