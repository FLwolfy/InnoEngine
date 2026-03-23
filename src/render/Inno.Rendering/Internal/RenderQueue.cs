using Inno.Rendering;

namespace Inno.Rendering;

internal enum RenderItemFilter
{
    Opaque = 0,
    Transparent,
    ShadowCasters,
    DepthOnly
}

internal readonly record struct RenderSortKey(ulong value) : IComparable<RenderSortKey>
{
    public int CompareTo(RenderSortKey other) => value.CompareTo(other.value);
}

internal readonly record struct ShaderPermutationKey(string surfaceType, string blendMode, string cullMode)
{
    public static ShaderPermutationKey FromMaterial(Material material)
    {
        return new ShaderPermutationKey(material.surfaceType.ToString(), material.blendMode.ToString(), material.cullMode.ToString());
    }
}

internal sealed class RenderItem
{
    public required Renderable renderable { get; init; }

    public required RenderSortKey sortKey { get; init; }
}

internal sealed class RenderQueue
{
    private readonly List<RenderItem> m_items = [];

    public IReadOnlyList<RenderItem> items => m_items;

    public void Add(RenderItem item) => m_items.Add(item);

    public void Sort() => m_items.Sort(static (a, b) => a.sortKey.CompareTo(b.sortKey));

    public void Clear() => m_items.Clear();
}

internal sealed class RenderList
{
    private readonly RenderScene m_scene;
    private readonly RenderView m_view;
    private readonly RenderQueue m_queue = new();

    public RenderList(RenderScene scene, RenderView view)
    {
        m_scene = scene;
        m_view = view;
    }

    public IReadOnlyList<RenderItem> items => m_queue.items;

    public void Build(RenderItemFilter filter)
    {
        m_queue.Clear();
        foreach (var renderable in m_scene.renderables.items)
        {
            if (renderable.visibility != Visibility.Visible)
            {
                continue;
            }

            var isTransparent = renderable is MeshRenderable meshRenderable && meshRenderable.material.surfaceType == MaterialSurfaceType.Transparent;
            if (filter == RenderItemFilter.Opaque && isTransparent)
            {
                continue;
            }

            if (filter == RenderItemFilter.Transparent && !isTransparent)
            {
                continue;
            }

            m_queue.Add(new RenderItem
            {
                renderable = renderable,
                sortKey = new RenderSortKey((ulong)(int.MaxValue - renderable.sortingOrder))
            });
        }

        m_queue.Sort();
    }
}
