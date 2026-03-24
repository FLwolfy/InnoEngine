namespace Inno.Rendering;

/// <summary>
/// Represents shadow settings for a light.
/// </summary>
public readonly record struct LightShadowSettings(
    bool enabled,
    int resolution,
    int cascadeCount,
    float cascadeSplitLambda,
    float depthBias,
    float normalBias,
    float strength,
    int pcfRadius)
{
    public static LightShadowSettings @default => new(
        enabled: true,
        resolution: 2048,
        cascadeCount: 2,
        cascadeSplitLambda: 0.75f,
        depthBias: 0.0012f,
        normalBias: 0.65f,
        strength: 0.65f,
        pcfRadius: 1);
}

/// <summary>
/// Represents a mutable scene light collection.
/// </summary>
public sealed class LightCollection
{
    private readonly List<Light> m_items = [];

    public IReadOnlyList<Light> items => m_items;

    public void Add(Light light)
    {
        ArgumentNullException.ThrowIfNull(light);
        m_items.Add(light);
    }

    public bool Remove(Light light)
    {
        ArgumentNullException.ThrowIfNull(light);
        return m_items.Remove(light);
    }

    public void Clear() => m_items.Clear();
}
