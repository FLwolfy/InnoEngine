using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents environment-level scene rendering properties.
/// </summary>
public sealed class SceneEnvironment
{
    public Color ambientColor { get; set; } = Color.BLACK;

    public float ambientIntensity { get; set; } = 1.0f;
}
