using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents omni-directional point light.
/// </summary>
public sealed class PointLight : Light
{
    public Vector3 position { get; set; }

    public float range { get; set; } = 10.0f;
}
