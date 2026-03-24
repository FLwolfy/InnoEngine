using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a user-defined shader material.
/// </summary>
public sealed class CustomMaterial : Material
{
    public string shaderName { get; set; } = string.Empty;

    public MaterialPropertyBlock properties { get; } = new();
}
