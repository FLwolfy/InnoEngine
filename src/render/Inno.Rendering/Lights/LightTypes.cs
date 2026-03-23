namespace Inno.Rendering;

/// <summary>
/// Represents shadow settings for a light.
/// </summary>
public readonly record struct LightShadowSettings(bool enabled, int resolution, float bias)
{
    public static LightShadowSettings @default => new(true, 2048, 0.0005f);
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
