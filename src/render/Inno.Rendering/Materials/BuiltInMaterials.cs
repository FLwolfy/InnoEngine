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

/// <summary>
/// Represents unlit shading settings.
/// </summary>
public sealed class UnlitMaterial : Material
{
    public Color color { get; set; } = Color.WHITE;

    public Texture2D? colorMap { get; set; }

    public float opacity { get; set; } = 1.0f;
}

/// <summary>
/// Represents sprite shading settings.
/// </summary>
public sealed class SpriteMaterial : Material
{
    public Color tint { get; set; } = Color.WHITE;

    public Texture2D? spriteTexture { get; set; }

    public bool pixelSnap { get; set; }
}

/// <summary>
/// Represents skybox shading settings.
/// </summary>
public sealed class SkyboxMaterial : Material
{
    public TextureCube? skyTexture { get; set; }

    public float exposure { get; set; } = 1.0f;
}

/// <summary>
/// Represents user-defined shader material.
/// </summary>
public sealed class CustomMaterial : Material
{
    public string shaderName { get; set; } = string.Empty;

    public MaterialPropertyBlock properties { get; } = new();
}
