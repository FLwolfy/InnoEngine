using System;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Bindings;

internal sealed class RenderableGpuBinding : IDisposable
{
    private const int C_PER_OBJECT_SET_INDEX = 0;
    private const int C_MATERIAL_SET_INDEX = 1;
    
    private readonly GpuCache.Handle<IPipelineState>[] m_psoHandles;

    private readonly MeshGpuBinding m_meshGpu;
    private readonly MaterialGpuBinding[] m_materialsGpu;
    private readonly PerObjectGpuBinding m_perObjectGpu;

    public RenderableGpuBinding(
        GpuCache.Handle<IPipelineState>[] psoHandles,
        MeshGpuBinding meshGpu,
        MaterialGpuBinding[] materialsGpu,
        PerObjectGpuBinding perObjectGpu)
    {
        if (materialsGpu.Length != psoHandles.Length)
        {
            throw new ArgumentException("materialsGpu and psoHandles must have same length.");
        }

        m_meshGpu = meshGpu;
        m_materialsGpu = materialsGpu;
        m_psoHandles = psoHandles;
        m_perObjectGpu = perObjectGpu;
    }

    public void UpdatePerObject<T>(ICommandList cmd, string name, T value) where T : unmanaged
    {
        m_perObjectGpu.Update(cmd, name, value);
    }

    public void DrawAll(ICommandList cmd)
    {
        for (int i = 0; i < m_meshGpu.segments.Length; i++)
        {
            DrawSegment(cmd, i);
        }
    }

    public void DrawSegment(ICommandList cmd, int segmentIndex)
    {
        var seg = m_meshGpu.segments[segmentIndex];
        int matIndex = seg.materialIndex;

        cmd.SetPipelineState(m_psoHandles[matIndex].value);
        
        m_meshGpu.Bind(cmd, segmentIndex);
        m_perObjectGpu.Bind(cmd, C_PER_OBJECT_SET_INDEX);
        m_materialsGpu[matIndex].Bind(cmd, C_MATERIAL_SET_INDEX);

        cmd.DrawIndexed((uint)seg.indexCount);
    }

    public void Dispose()
    {
        m_perObjectGpu.Dispose();
        foreach (var h in m_psoHandles) h.Dispose();
        foreach (var m in m_materialsGpu) m.Dispose();
        m_meshGpu.Dispose();
    }
}
