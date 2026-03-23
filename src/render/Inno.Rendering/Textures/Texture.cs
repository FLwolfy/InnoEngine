namespace Inno.Rendering;

/// <summary>
/// Represents a high-level texture asset.
/// </summary>
public abstract class Texture
{
    public int width { get; protected init; }

    public int height { get; protected init; }

    public TextureFormat format { get; protected init; } = TextureFormat.Unknown;
}

/// <summary>
/// Represents a 2D texture.
/// </summary>
public sealed class Texture2D : Texture
{
    public Texture2D(int width, int height, TextureFormat format)
    {
        this.width = width;
        this.height = height;
        this.format = format;
    }
}

/// <summary>
/// Represents a cube texture.
/// </summary>
public sealed class TextureCube : Texture
{
    public TextureCube(int size, TextureFormat format)
    {
        width = size;
        height = size;
        this.format = format;
    }
}

/// <summary>
/// Represents a renderable texture.
/// </summary>
public sealed class RenderTexture : Texture
{
    public RenderTexture(int width, int height, TextureFormat format, bool hasDepth, bool hasMipmaps)
    {
        this.width = width;
        this.height = height;
        this.format = format;
        this.hasDepth = hasDepth;
        this.hasMipmaps = hasMipmaps;
    }

    public bool hasDepth { get; }

    public bool hasMipmaps { get; }
}
