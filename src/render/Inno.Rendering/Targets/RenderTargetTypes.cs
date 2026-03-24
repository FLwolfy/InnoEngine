
namespace Inno.Rendering;

/// <summary>
/// Represents render target color/depth formats.
/// </summary>
public enum RenderTargetFormat
{
    Rgba8 = 0,
    Rgba16Float,
    Depth24Stencil8,
    Depth32
}

/// <summary>
/// Represents render target dimensions.
/// </summary>
public readonly record struct RenderTargetSize(int width, int height);

/// <summary>
/// Represents render target creation settings.
/// </summary>
public sealed class RenderTargetDescriptor
{
    public RenderTargetSize size { get; init; }

    public RenderTargetFormat colorFormat { get; init; } = RenderTargetFormat.Rgba8;

    public bool hasDepth { get; init; } = true;

    public bool hasMipmaps { get; init; }
}

/// <summary>
/// Represents a minimal window placeholder for backbuffer targets.
/// </summary>
public sealed class RenderWindow
{
    public required IntPtr nativeHandle { get; init; }

    public int width { get; set; }

    public int height { get; set; }
}
