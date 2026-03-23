using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Describes graphics texture creation.
/// </summary>
public sealed class TextureDescription
{
    public int width { get; init; }

    public int height { get; init; }

    public int depthOrLayers { get; init; } = 1;

    public int mipLevels { get; init; } = 1;

    public TextureDimension dimension { get; init; } = TextureDimension.Texture2D;

    public TextureUsage usage { get; init; } = TextureUsage.Sampled;

    public PixelFormat format { get; init; } = PixelFormat.R8G8B8A8Unorm;
}
