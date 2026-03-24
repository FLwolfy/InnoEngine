
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

/// <summary>
/// Represents window backbuffer output.
/// </summary>
public sealed class BackbufferTarget : RenderTarget
{
    public BackbufferTarget(RenderWindow window)
    {
        this.window = window;
    }

    public RenderWindow window { get; }

    public override int width => window.width;

    public override int height => window.height;

    public override Texture? colorTexture => null;

    public override Texture? depthTexture => null;
}

/// <summary>
/// Represents an offscreen texture render target.
/// </summary>
public sealed class TextureRenderTarget : RenderTarget
{
    public TextureRenderTarget(RenderTargetDescriptor descriptor)
    {
        this.descriptor = descriptor;
        colorTexture = new RenderTexture(descriptor.size.width, descriptor.size.height, Map(descriptor.colorFormat), descriptor.hasDepth, descriptor.hasMipmaps);
        depthTexture = descriptor.hasDepth
            ? new RenderTexture(descriptor.size.width, descriptor.size.height, TextureFormat.Depth24Stencil8, true, false)
            : null;
    }

    public RenderTargetDescriptor descriptor { get; }

    public override int width => descriptor.size.width;

    public override int height => descriptor.size.height;

    public override Texture? colorTexture { get; }

    public override Texture? depthTexture { get; }

    private static TextureFormat Map(RenderTargetFormat format)
    {
        return format switch
        {
            RenderTargetFormat.Rgba8 => TextureFormat.Rgba8,
            RenderTargetFormat.Rgba16Float => TextureFormat.Rgba16Float,
            RenderTargetFormat.Depth24Stencil8 => TextureFormat.Depth24Stencil8,
            RenderTargetFormat.Depth32 => TextureFormat.Depth32,
            _ => TextureFormat.Unknown
        };
    }
}
