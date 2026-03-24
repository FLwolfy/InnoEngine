namespace Inno.Rendering;

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
