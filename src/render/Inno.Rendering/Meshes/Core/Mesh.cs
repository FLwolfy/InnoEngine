namespace Inno.Rendering;

/// <summary>
/// Represents mesh geometry and surfaces.
/// </summary>
public sealed class Mesh
{
    private readonly List<MeshSurface> m_surfaces = [];
    private byte[] m_vertexData = [];
    private uint[] m_indices = [];

    public string name { get; set; } = string.Empty;

    public MeshBounds bounds { get; set; }

    public VertexLayout vertexLayout { get; private set; } = new([], 0);

    public IReadOnlyList<MeshSurface> surfaces => m_surfaces;

    public int vertexCount { get; private set; }

    public int indexCount { get; private set; }

    internal ReadOnlyMemory<byte> vertexData => m_vertexData;

    internal ReadOnlyMemory<uint> indices => m_indices;

    public void SetVertices<TVertex>(ReadOnlySpan<TVertex> vertices) where TVertex : unmanaged
    {
        vertexCount = vertices.Length;
        vertexLayout = InferLayout<TVertex>();
        m_vertexData = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices).ToArray();
    }

    public void SetIndices(ReadOnlySpan<uint> indices)
    {
        indexCount = indices.Length;
        m_indices = indices.ToArray();
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
        if (typeof(TVertex) == typeof(StandardVertex))
        {
            var standardStride = System.Runtime.InteropServices.Marshal.SizeOf<StandardVertex>();
            if (standardStride != 64)
            {
                throw new InvalidOperationException($"StandardVertex stride mismatch. Expected 64, actual {standardStride}.");
            }

            return new VertexLayout([
                new VertexElement(VertexSemantic.Position, 0, 0, 12),
                new VertexElement(VertexSemantic.Normal, 0, 12, 12),
                new VertexElement(VertexSemantic.Tangent, 0, 24, 16),
                new VertexElement(VertexSemantic.TexCoord0, 0, 40, 8),
                new VertexElement(VertexSemantic.Color0, 0, 48, 16)
            ], 64);
        }

        var stride = System.Runtime.InteropServices.Marshal.SizeOf<TVertex>();
        return new VertexLayout([new VertexElement(VertexSemantic.Position, 0, 0, stride)], stride);
    }
}
