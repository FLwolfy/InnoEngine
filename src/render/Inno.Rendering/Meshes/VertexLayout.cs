namespace Inno.Rendering;

/// <summary>
/// Represents a semantic-aware vertex layout.
/// </summary>
public sealed class VertexLayout
{
    private readonly List<VertexElement> m_elements = [];

    public VertexLayout(IEnumerable<VertexElement> elements, int stride)
    {
        m_elements.AddRange(elements);
        this.stride = stride;
    }

    public IReadOnlyList<VertexElement> elements => m_elements;

    public int stride { get; }

    public static VertexLayoutBuilder CreateBuilder() => new();
}
