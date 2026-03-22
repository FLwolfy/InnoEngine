using System;
using Inno.Engine.Graphics.Resources.CpuResources;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for mesh resources.
/// Does not reference CPU <see cref="CpuResources.Mesh"/> and does not allocate GPU resources.
/// </summary>
internal sealed class MeshGpuBinding : IDisposable
{
    private readonly GpuCache.Handle<IVertexBuffer> m_vbHandle;
    private readonly GpuCache.Handle<IIndexBuffer>[] m_ibHandles;
    public MeshSegment[] segments { get; }

    public MeshGpuBinding(
        MeshSegment[] segments,
        GpuCache.Handle<IVertexBuffer> vbHandle,
        GpuCache.Handle<IIndexBuffer>[] ibHandles)
    {
        m_vbHandle = vbHandle;
        m_ibHandles = ibHandles;
        this.segments = segments;
    }

    public void Bind(ICommandList cmd, int segmentIndex)
    {
        cmd.SetVertexBuffer(m_vbHandle.value);
        cmd.SetIndexBuffer(m_ibHandles[segmentIndex].value);
    }

    public void Dispose()
    {
        m_vbHandle.Dispose();
        foreach (var h in m_ibHandles) h.Dispose();
    }
}
