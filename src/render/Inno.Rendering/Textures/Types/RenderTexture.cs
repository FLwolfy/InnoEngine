namespace Inno.Rendering;

/// <summary>
/// Represents a texture that can be used as a render target.
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
