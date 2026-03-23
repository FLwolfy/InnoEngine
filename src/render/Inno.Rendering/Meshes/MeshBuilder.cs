using System.Collections;
using System.Runtime.InteropServices;

namespace Inno.Rendering;

/// <summary>
/// Builds mesh data from typed vertices or semantic streams.
/// </summary>
public sealed class MeshBuilder
{
    private readonly Dictionary<(VertexSemantic Semantic, int Index), Array> m_streams = [];
    private readonly List<uint> m_indices = [];
    private readonly List<MeshSurface> m_surfaces = [];
    private Array? m_typedVertices;
    private VertexLayout? m_semanticLayout;

    public MeshBuilder SetVertices<TVertex>(ReadOnlySpan<TVertex> vertices) where TVertex : unmanaged
    {
        m_typedVertices = vertices.ToArray();
        return this;
    }

    public MeshBuilder SetSemanticStream<T>(VertexSemantic semantic, int semanticIndex, ReadOnlySpan<T> values) where T : unmanaged
    {
        m_streams[(semantic, semanticIndex)] = values.ToArray();
        return this;
    }

    public MeshBuilder SetSemanticLayout(VertexLayout layout)
    {
        m_semanticLayout = layout ?? throw new ArgumentNullException(nameof(layout));
        return this;
    }

    public MeshBuilder SetIndices(ReadOnlySpan<uint> indices)
    {
        m_indices.Clear();
        m_indices.AddRange(indices.ToArray());
        return this;
    }

    public MeshBuilder AddSurface(MeshSurface surface)
    {
        m_surfaces.Add(surface);
        return this;
    }

    public Mesh Build(string? name = null)
    {
        var mesh = new Mesh
        {
            name = name ?? "Mesh"
        };

        if (m_typedVertices is not null)
        {
            ApplyTypedVertices(mesh, m_typedVertices);
        }
        else if (m_semanticLayout is not null)
        {
            var vertexCount = m_streams.Count == 0 ? 0 : m_streams.Values.Min(x => x.Length);
            mesh.SetVertices<int>(new int[vertexCount]);
        }

        mesh.SetIndices(CollectionsMarshal.AsSpan(m_indices));

        if (m_surfaces.Count == 0 && m_indices.Count > 0)
        {
            mesh.SetSurface(0, new MeshSurface(0, m_indices.Count, 0, MeshTopology.Triangles));
        }
        else
        {
            for (var i = 0; i < m_surfaces.Count; i++)
            {
                mesh.SetSurface(i, m_surfaces[i]);
            }
        }

        return mesh;
    }

    private static void ApplyTypedVertices(Mesh mesh, Array vertices)
    {
        switch (vertices)
        {
            case StandardVertex[] standardVertices:
                mesh.SetVertices<StandardVertex>(standardVertices);
                break;
            case int[] intVertices:
                mesh.SetVertices<int>(intVertices);
                break;
            default:
                mesh.SetVertices<int>(new int[vertices.Length]);
                break;
        }
    }
}
