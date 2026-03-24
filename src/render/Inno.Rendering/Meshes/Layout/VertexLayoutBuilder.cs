namespace Inno.Rendering;

/// <summary>
/// Builds immutable vertex layouts.
/// </summary>
public sealed class VertexLayoutBuilder
{
    private readonly List<VertexElement> m_elements = [];
    private int m_stride;

    public VertexLayoutBuilder Add(VertexSemantic semantic, int semanticIndex, int sizeInBytes)
    {
        if (sizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
        }

        m_elements.Add(new VertexElement(semantic, semanticIndex, m_stride, sizeInBytes));
        m_stride += sizeInBytes;
        return this;
    }

    public VertexLayout Build()
    {
        return new VertexLayout(m_elements, m_stride);
    }
}
