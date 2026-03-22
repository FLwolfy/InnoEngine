using System;
using System.Collections.Generic;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for per-object resources (typically per instance / renderable).
/// All GPU allocations must be performed by a compiler.
/// </summary>
internal sealed class PerObjectGpuBinding : IDisposable
{
    private readonly Dictionary<string, int> m_index;
    private readonly GpuCache.Handle<IUniformBuffer>[] m_uniformHandles;
    private readonly GpuCache.Handle<IResourceSet> m_resourceSetHandle;

    public IUniformBuffer[] uniformBuffers => Array.ConvertAll(m_uniformHandles, h => h.value);

    public PerObjectGpuBinding(
        Dictionary<string, int> indexMap,
        GpuCache.Handle<IUniformBuffer>[] uniformHandles,
        GpuCache.Handle<IResourceSet> resourceSetHandle)
    {
        m_index = indexMap;
        m_uniformHandles = uniformHandles;
        m_resourceSetHandle = resourceSetHandle;
    }

    public void Update<T>(ICommandList cmd, string name, T value) where T : unmanaged
    {
        if (!m_index.TryGetValue(name, out var idx))
            throw new InvalidOperationException($"PerObjectUniform '{name}' not registered.");

        cmd.UpdateUniform(m_uniformHandles[idx].value, ref value);
    }

    public void Bind(ICommandList cmd, int perObjectSetId)
    {
        cmd.SetResourceSet(perObjectSetId, m_resourceSetHandle.value);
    }

    public void Dispose()
    {
        foreach (var h in m_uniformHandles) h.Dispose();
        m_resourceSetHandle.Dispose();
    }
}
