using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents shared light settings.
/// </summary>
public abstract class Light
{
    public Color color { get; set; } = Color.WHITE;

    public float intensity { get; set; } = 1.0f;

    public bool enabled { get; set; } = true;

    public LightShadowSettings shadows { get; set; } = LightShadowSettings.@default;
}

/// <summary>
/// Represents directional sunlight-like light.
/// </summary>
public sealed class DirectionalLight : Light
{
    public Vector3 direction { get; set; } = Vector3.NormalizeSafe(new Vector3(0.2f, -1.0f, 0.3f));
}

/// <summary>
/// Represents omni-directional point light.
/// </summary>
public sealed class PointLight : Light
{
    public Vector3 position { get; set; }

    public float range { get; set; } = 10.0f;
}

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
