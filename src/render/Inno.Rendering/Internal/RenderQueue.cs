namespace Inno.Rendering;

internal sealed class RenderQueue
{
    private readonly List<RenderItem> m_items = [];

    public IReadOnlyList<RenderItem> items => m_items;

    public void Add(RenderItem item) => m_items.Add(item);

    public void Sort() => m_items.Sort(static (a, b) => a.sortKey.CompareTo(b.sortKey));

    public void Clear() => m_items.Clear();
}
