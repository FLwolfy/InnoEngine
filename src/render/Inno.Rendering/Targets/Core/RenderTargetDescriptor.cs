
namespace Inno.Rendering;

/// <summary>
/// Represents offscreen render target creation settings.
/// </summary>
public sealed class RenderTargetDescriptor
{
    public RenderTargetSize size { get; init; }

    public RenderTargetFormat colorFormat { get; init; } = RenderTargetFormat.Rgba8;

    public bool hasDepth { get; init; } = true;

    public bool hasMipmaps { get; init; }
}
