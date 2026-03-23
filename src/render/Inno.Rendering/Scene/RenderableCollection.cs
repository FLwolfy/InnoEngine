namespace Inno.Rendering;

/// <summary>
/// Represents a mutable collection of renderables.
/// </summary>
public sealed class RenderableCollection
{
    private readonly List<Renderable> m_items = [];

    public IReadOnlyList<Renderable> items => m_items;

    public void Add(Renderable renderable)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        m_items.Add(renderable);
    }

    public bool Remove(Renderable renderable)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        return m_items.Remove(renderable);
    }

    public void Clear() => m_items.Clear();
}
