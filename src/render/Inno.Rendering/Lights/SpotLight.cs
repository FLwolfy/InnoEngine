using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents cone-shaped spot light.
/// </summary>
public sealed class SpotLight : Light
{
    public Vector3 position { get; set; }

    public Vector3 direction { get; set; } = Vector3.DOWN;

    public float range { get; set; } = 15.0f;

    public float innerAngle { get; set; } = 25.0f;

    public float outerAngle { get; set; } = 35.0f;
}
