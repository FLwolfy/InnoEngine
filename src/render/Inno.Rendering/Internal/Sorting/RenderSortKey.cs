
namespace Inno.Rendering;

internal readonly record struct RenderSortKey(ulong value) : IComparable<RenderSortKey>
{
    public int CompareTo(RenderSortKey other) => value.CompareTo(other.value);
}

