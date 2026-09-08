using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderGeometryOwner : RenderResourceProvider, IDisposable
{
    private readonly IRenderDevice m_device;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly RenderResourceCache<Guid, Entry> m_entries;
    private RenderRetirementQueue? m_allocation;
    private RenderRetirementQueue? m_retirement;
    private ulong m_frameIndex;

    internal RenderGeometryOwner(IRenderDevice device, IDiagnosticReporter diagnostics, int capacity)
    {
        m_device = device;
        m_diagnostics = diagnostics;
        m_entries = new(entry => entry.retirement.Dispose(), capacity);
    }

    internal int count => m_entries.Count;
    internal int retiringCount => m_entries.pendingCount;
    internal long rejectedCount => m_entries.rejectedCount;

    internal void BeginFrame(ulong frameIndex)
    {
        Drain();
        m_frameIndex = frameIndex;
    }

    internal void Drain()
    {
        ObjectDisposedException.ThrowIf(m_retirement is not null, this);
        if (m_allocation is not null)
        {
            m_allocation.Dispose();
            m_allocation = null;
        }
        m_entries.Drain();
    }

    internal void Sweep(ulong oldest) => m_entries.Sweep(entry => entry.lastUsedFrame < oldest);

    internal bool TryResolve(GeometryAsset asset, out RenderGeometry? geometry)
    {
        Drain();
        ArgumentNullException.ThrowIfNull(asset);
        geometry = null;
        Guid id = asset.identity.persistentId;
        string source = asset.assetPath.ToString();
        if (id == Guid.Empty)
        {
            m_diagnostics.Publish(new Diagnostic("RENDER_GEOMETRY_ID_MISSING",
                "Geometry must have a persistent asset identity.", DiagnosticSeverity.Error, source));
            return false;
        }
        m_entries.TryGetValue(id, out Entry? entry);
        if (entry is null || entry.revision != asset.contentVersion)
        {
            m_entries.RequireCapacity(id);
            Entry candidate;
            try { candidate = Create(asset); }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
            catch (Exception failure)
            {
                m_diagnostics.Publish(new Diagnostic("RENDER_GEOMETRY_RESOLVE_FAILED",
                    $"Geometry '{source}' kept its complete last-good buffer pair: {failure.Message}",
                    DiagnosticSeverity.Error, source));
                if (entry is null) return false;
                entry.lastUsedFrame = m_frameIndex;
                geometry = entry.geometry;
                return true;
            }
            m_entries.Replace(id, candidate);
            m_entries.Drain();
            entry = candidate;
            m_diagnostics.Resolve("RENDER_GEOMETRY_RESOLVE_FAILED", source);
        }
        entry.lastUsedFrame = m_frameIndex;
        geometry = entry.geometry;
        return true;
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_retirement is null)
        {
            m_retirement = new RenderRetirementQueue();
            if (m_allocation is not null) m_retirement.Add(m_allocation.Dispose);
            m_retirement.Add(m_entries.Dispose);
        }
        m_retirement.Dispose();
        m_allocation = null;
    }

    private Entry Create(GeometryAsset asset)
    {
        GeometryData data = GeometryAssetRuntime.GetGeometryData(asset);
        RenderVertexLayout layout = CanonicalGeometryLayout();
        byte[] vertices = PackVertices(data.vertices);
        byte[] indices = MemoryMarshal.AsBytes(data.indices.ToArray().AsSpan()).ToArray();
        IReadOnlyList<RenderGeometrySection> sections = Array.AsReadOnly(data.sections.Select(static section =>
            new RenderGeometrySection(section.firstIndex, section.indexCount)).ToArray());
        m_allocation = new RenderRetirementQueue();
        try
        {
            PersistentBufferHandle vertex = m_device.CreateBuffer(new PersistentBufferDescriptor(
                new RenderBufferDescriptor(data.vertices.Count, layout.stride, RenderBufferUsage.Vertex), layout),
                vertices, $"{asset.name}/Vertices");
            m_allocation.Add(() => m_device.DestroyBuffer(vertex));
            PersistentBufferHandle index = m_device.CreateBuffer(new PersistentBufferDescriptor(
                new RenderBufferDescriptor(data.indices.Count, sizeof(uint), RenderBufferUsage.Index),
                indexFormat: RenderIndexFormat.UInt32), indices, $"{asset.name}/Indices");
            m_allocation.Add(() => m_device.DestroyBuffer(index));
            var entry = new Entry(CreateGeometry(vertex, index, layout, data.vertices.Count,
                data.indices.Count, sections), asset.contentVersion, m_frameIndex, m_allocation);
            m_allocation = null;
            return entry;
        }
        catch (Exception failure)
        {
            try { m_allocation?.Dispose(); }
            catch (Exception retirement) { throw new AggregateException("Geometry allocation and retirement failed.", failure, retirement); }
            m_allocation = null;
            throw;
        }
    }

    private static RenderVertexLayout CanonicalGeometryLayout()
        => new([
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Normal, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Tangent, RenderVertexFormat.Float4),
            new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2)
        ]);

    private static byte[] PackVertices(IReadOnlyList<GeometryVertex> vertices)
    {
        var values = new float[checked(vertices.Count * 12)];
        int offset = 0;
        foreach (GeometryVertex vertex in vertices)
        {
            values[offset++] = vertex.position.x;
            values[offset++] = vertex.position.y;
            values[offset++] = vertex.position.z;
            values[offset++] = vertex.normal.x;
            values[offset++] = vertex.normal.y;
            values[offset++] = vertex.normal.z;
            values[offset++] = vertex.tangent.x;
            values[offset++] = vertex.tangent.y;
            values[offset++] = vertex.tangent.z;
            values[offset++] = vertex.tangent.w;
            values[offset++] = vertex.textureCoordinate.x;
            values[offset++] = vertex.textureCoordinate.y;
        }
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    private sealed class Entry(RenderGeometry geometry, long revision, ulong frameIndex, RenderRetirementQueue retirement)
    {
        internal RenderGeometry geometry { get; } = geometry;
        internal long revision { get; } = revision;
        internal ulong lastUsedFrame { get; set; } = frameIndex;
        internal RenderRetirementQueue retirement { get; } = retirement;
    }
}
