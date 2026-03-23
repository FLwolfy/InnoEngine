namespace Inno.Rendering;

/// <summary>
/// Represents mesh geometry and surfaces.
/// </summary>
public sealed class Mesh
{
    private readonly List<MeshSurface> m_surfaces = [];

    public string name { get; set; } = string.Empty;

    public MeshBounds bounds { get; set; }

    public VertexLayout vertexLayout { get; private set; } = new([], 0);

    public IReadOnlyList<MeshSurface> surfaces => m_surfaces;

    public int vertexCount { get; private set; }

    public int indexCount { get; private set; }

    public void SetVertices<TVertex>(ReadOnlySpan<TVertex> vertices) where TVertex : unmanaged
    {
        vertexCount = vertices.Length;
        vertexLayout = InferLayout<TVertex>();
    }

    public void SetIndices(ReadOnlySpan<uint> indices)
    {
        indexCount = indices.Length;
    }

    public void SetSurface(int surfaceIndex, MeshSurface surface)
    {
        if (surfaceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
        }

        while (m_surfaces.Count <= surfaceIndex)
        {
            m_surfaces.Add(default);
        }

        m_surfaces[surfaceIndex] = surface;
    }

    private static VertexLayout InferLayout<TVertex>() where TVertex : unmanaged
    {
        var stride = System.Runtime.InteropServices.Marshal.SizeOf<TVertex>();
        return new VertexLayout([new VertexElement(VertexSemantic.Position, 0, 0, stride)], stride);
    }
}
