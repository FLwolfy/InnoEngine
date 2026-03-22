using System;
using Inno.Engine.Graphics.Resources.GpuResources.Cache;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.GpuResources.Bindings;

/// <summary>
/// Pure GPU container for a material instance.
/// - Owns material uniform buffers (via <see cref="GpuCache"/> handles)
/// - Owns texture/sampler/shader handles (usually shared)
/// - Owns material resource set (via <see cref="GpuCache"/> handle)
/// </summary>
internal sealed class MaterialGpuBinding : IDisposable
{
    private readonly GpuCache.Handle<IUniformBuffer>[] m_ubHandles;
    private readonly GpuCache.Handle<ITexture>[] m_texHandles;
    private readonly GpuCache.Handle<ISampler>[] m_smpHandles;
    private readonly GpuCache.Handle<IShader> m_vsHandle;
    private readonly GpuCache.Handle<IShader> m_fsHandle;
    private readonly GpuCache.Handle<IResourceSet> m_resourceSetHandle;

    public IUniformBuffer[] uniformBuffers => Array.ConvertAll(m_ubHandles, h => h.value);
    public ITexture[] textures => Array.ConvertAll(m_texHandles, h => h.value);
    public ISampler[] samplers => Array.ConvertAll(m_smpHandles, h => h.value);

    public IShader vertexShader => m_vsHandle.value;
    public IShader fragmentShader => m_fsHandle.value;
    public IResourceSet resourceSet => m_resourceSetHandle.value;

    public MaterialGpuBinding(
        GpuCache.Handle<IUniformBuffer>[] ubHandles,
        GpuCache.Handle<ITexture>[] texHandles,
        GpuCache.Handle<ISampler>[] smpHandles,
        GpuCache.Handle<IShader> vsHandle,
        GpuCache.Handle<IShader> fsHandle,
        GpuCache.Handle<IResourceSet> resourceSetHandle)
    {
        m_ubHandles = ubHandles;
        m_texHandles = texHandles;
        m_smpHandles = smpHandles;
        m_vsHandle = vsHandle;
        m_fsHandle = fsHandle;
        m_resourceSetHandle = resourceSetHandle;
    }

    public void Bind(ICommandList cmd, int materialSetId)
    {
        cmd.SetResourceSet(materialSetId, resourceSet);
    }

    public void Dispose()
    {
        foreach (var h in m_ubHandles) h.Dispose();
        m_resourceSetHandle.Dispose();

        foreach (var h in m_texHandles) h.Dispose();
        foreach (var h in m_smpHandles) h.Dispose();

        m_vsHandle.Dispose();
        m_fsHandle.Dispose();
    }
}
