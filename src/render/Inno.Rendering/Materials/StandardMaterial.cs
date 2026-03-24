using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents metallic-roughness PBR material settings.
/// </summary>
public sealed class StandardMaterial : Material
{
    public Color baseColor { get; set; } = Color.WHITE;

    public Texture2D? baseMap { get; set; }

    public float metallic { get; set; }

    public float roughness { get; set; } = 1.0f;

    public Texture2D? metallicRoughnessMap { get; set; }

    public Texture2D? normalMap { get; set; }

    public float normalScale { get; set; } = 1.0f;

    public Texture2D? occlusionMap { get; set; }

    public float occlusionStrength { get; set; } = 1.0f;

    public Color emissiveColor { get; set; } = Color.BLACK;

    public Texture2D? emissiveMap { get; set; }

    public float alphaCutoff { get; set; } = 0.5f;

    public bool doubleSided { get; set; }
}
