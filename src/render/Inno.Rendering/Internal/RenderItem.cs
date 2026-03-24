
namespace Inno.Rendering;

internal sealed class RenderItem
{
    public required Renderable renderable { get; init; }

    public required RenderSortKey sortKey { get; init; }
}

