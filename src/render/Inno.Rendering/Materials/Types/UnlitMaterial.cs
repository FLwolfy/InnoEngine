using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents unlit shading settings.
/// </summary>
public sealed class UnlitMaterial : Material
{
    public Color color { get; set; } = Color.WHITE;

    public Texture2D? colorMap { get; set; }

    public float opacity { get; set; } = 1.0f;
}
