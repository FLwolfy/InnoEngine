namespace Inno.Rendering;

/// <summary>
/// Represents a stack of post-process effects.
/// </summary>
public sealed class PostProcessStack
{
    private readonly List<PostProcessEffect> m_effects = [];

    public IReadOnlyList<PostProcessEffect> effects => m_effects;

    public void Add(PostProcessEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        m_effects.Add(effect);
    }

    public bool Remove(PostProcessEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return m_effects.Remove(effect);
    }

    public void Clear() => m_effects.Clear();
}
