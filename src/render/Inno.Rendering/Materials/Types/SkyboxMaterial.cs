using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents skybox shading settings.
/// </summary>
public sealed class SkyboxMaterial : Material
{
    public TextureCube? skyTexture { get; set; }

    public float exposure { get; set; } = 1.0f;
}
