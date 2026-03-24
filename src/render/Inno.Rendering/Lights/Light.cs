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
