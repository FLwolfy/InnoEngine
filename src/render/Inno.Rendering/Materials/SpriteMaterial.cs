using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents sprite shading settings.
/// </summary>
public sealed class SpriteMaterial : Material
{
    public Color tint { get; set; } = Color.WHITE;

    public Texture2D? spriteTexture { get; set; }

    public bool pixelSnap { get; set; }
}
