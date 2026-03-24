
namespace Inno.Rendering;

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
