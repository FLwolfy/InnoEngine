using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents directional sunlight-like light.
/// </summary>
public sealed class DirectionalLight : Light
{
    public Vector3 direction { get; set; } = Vector3.NormalizeSafe(new Vector3(0.2f, -1.0f, 0.3f));
}
