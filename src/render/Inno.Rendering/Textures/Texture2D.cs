namespace Inno.Rendering;

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
