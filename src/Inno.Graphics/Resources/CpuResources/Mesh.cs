using System;
using System.Collections.Generic;
using Inno.Platform.Graphics;

namespace Inno.Graphics.Resources.CpuResources;

public struct MeshRenderState
{
    public PrimitiveTopology topology;
}

public readonly struct MeshSegment(string name, int indexStart, int indexCount, int materialIndex)
{
    public string name { get; } = name;
    public int indexStart { get; } = indexStart;
    public int indexCount { get; } = indexCount;
    public int materialIndex { get; } = materialIndex;
}

public class Mesh
{
    public Guid guid { get; }

    private readonly List<VertexAttributeEntry> m_attributes = new();
    private readonly Dictionary<string, int> m_attributeIndex = new();

    private readonly List<MeshSegment> m_segments = new();
    private uint[] m_indices = [];

    public string name { get; }
    public MeshRenderState renderState { get; set; }

    public int vertexCount => m_attributes.Count == 0 ? 0 : m_attributes[0].data.Length;
    public int indexCount => m_indices.Length;
    public int segmentCount => m_segments.Count;

    public Mesh(string name)
    {
        this.guid = Guid.NewGuid();
        this.name = name;
    }
    
    public Mesh(Guid guid, string name)
    {
        this.guid = guid;
        this.name = name;
    }

    public readonly struct VertexAttributeEntry(string name, Type type, Array data)
    {
        public readonly string name = name;
        public readonly Type elementType = type;
        public readonly Array data = data;
    }

    public void SetAttribute<T>(string attributeName, T[] data) where T : unmanaged
    {
        if (m_attributeIndex.TryGetValue(attributeName, out var idx))
            m_attributes[idx] = new VertexAttributeEntry(attributeName, typeof(T), data);
        else
        {
            m_attributes.Add(new VertexAttributeEntry(attributeName, typeof(T), data));
            m_attributeIndex[attributeName] = m_attributes.Count - 1;
        }
    }

    public T[] GetAttribute<T>(string attributeName) where T : unmanaged
        => (T[])m_attributes[m_attributeIndex[attributeName]].data;

    public IReadOnlyList<VertexAttributeEntry> GetAllAttributes() => m_attributes;

    public void SetIndices(uint[] indices) => m_indices = indices;
    public uint[] GetIndices() => m_indices;

    public void AddSegment(MeshSegment meshSegment) => m_segments.Add(meshSegment);
    public IReadOnlyList<MeshSegment> GetSegments() => m_segments;
}
