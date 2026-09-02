using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderFrameUploadService : RenderFrameUploadProvider, IRenderFrameUploadService, IDisposable
{
    private const int C_MINIMUM_PAGE_ELEMENTS = 256;
    private const ulong C_UNUSED_FRAME_LIMIT = 240;

    private readonly IRenderDevice m_device;
    private readonly Dictionary<UploadPageKey, UploadPool> m_pools = [];
    private ulong m_frameIndex;
    private bool m_frameOpen;
    private bool m_disposed;

    internal RenderFrameUploadService(IRenderDevice device)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>
    /// Uploads immutable frame data and returns the allocated transient buffer slice.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by upload buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="data">
    /// The complete immutable byte payload consumed by this operation.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated render buffer slice that represents the completed operation.
    /// </returns>
    public RenderBufferSlice UploadBuffer(
        RenderBufferUploadDescriptor descriptor,
        ReadOnlyMemory<byte> data,
        string name)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_frameOpen)
            throw new InvalidOperationException("Frame uploads are only accepted during an open render frame.");
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (data.IsEmpty || data.Length % descriptor.elementStride != 0)
        {
            throw new ArgumentException(
                "Upload data must contain one or more complete elements.",
                nameof(data));
        }

        int elementCount = data.Length / descriptor.elementStride;
        var key = new UploadPageKey(
            descriptor.elementStride,
            descriptor.usage,
            descriptor.vertexLayout,
            descriptor.indexFormat);
        if (!m_pools.TryGetValue(key, out UploadPool? pool))
        {
            pool = new UploadPool();
            m_pools.Add(key, pool);
        }

        bool requiresDedicatedPage = (descriptor.usage & RenderBufferUsage.Storage) != 0;
        UploadPage? page = requiresDedicatedPage
            ? pool.pages.FirstOrDefault(candidate =>
                !candidate.usedThisFrame && candidate.capacity >= elementCount)
            : pool.pages.FirstOrDefault(candidate =>
                candidate.capacity - candidate.writeOffset >= elementCount);
        if (page is null)
        {
            int capacity = NextPowerOfTwo(Math.Max(C_MINIMUM_PAGE_ELEMENTS, elementCount));
            RenderBufferUsage usage = descriptor.usage | RenderBufferUsage.Dynamic;
            var bufferDescriptor = new PersistentBufferDescriptor(
                new RenderBufferDescriptor(capacity, descriptor.elementStride, usage),
                descriptor.vertexLayout,
                descriptor.indexFormat);
            PersistentBufferHandle handle = m_device.CreateBuffer(
                bufferDescriptor,
                ReadOnlySpan<byte>.Empty,
                $"{name}/FrameUpload[{pool.pages.Count}]");
            page = new UploadPage(handle, capacity);
            pool.pages.Add(page);
        }

        int firstElement = requiresDedicatedPage ? 0 : page.writeOffset;
        m_device.UpdateBuffer(page.handle, data.Span, firstElement);
        page.writeOffset = checked(firstElement + elementCount);
        page.usedThisFrame = true;
        page.lastUsedFrame = m_frameIndex;
        return CreateBufferSlice(
            page.handle,
            firstElement,
            elementCount,
            descriptor.usage,
            m_frameIndex);
    }

    internal void BeginFrame(ulong frameIndex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_frameIndex = frameIndex;
        m_frameOpen = true;
        foreach (UploadPage page in m_pools.Values.SelectMany(static pool => pool.pages))
        {
            page.writeOffset = 0;
            page.usedThisFrame = false;
        }
    }

    internal void EndFrame()
    {
        if (!m_disposed)
            m_frameOpen = false;
    }

    internal void SweepUnused()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_frameIndex < C_UNUSED_FRAME_LIMIT)
            return;
        ulong oldest = m_frameIndex - C_UNUSED_FRAME_LIMIT;
        foreach (UploadPageKey key in m_pools.Keys.ToArray())
        {
            UploadPool pool = m_pools[key];
            foreach (UploadPage page in pool.pages
                         .Where(candidate => candidate.lastUsedFrame < oldest)
                         .ToArray())
            {
                m_device.DestroyBuffer(page.handle);
                pool.pages.Remove(page);
            }
            if (pool.pages.Count == 0)
                m_pools.Remove(key);
        }
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_frameOpen = false;
        foreach (UploadPage page in m_pools.Values.SelectMany(static pool => pool.pages))
            m_device.DestroyBuffer(page.handle);
        m_pools.Clear();
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result = checked(result << 1);
        return result;
    }

    private readonly record struct UploadPageKey(
        int elementStride,
        RenderBufferUsage usage,
        RenderVertexLayout? vertexLayout,
        RenderIndexFormat indexFormat);

    private sealed class UploadPool
    {
        internal List<UploadPage> pages { get; } = [];
    }

    private sealed class UploadPage(PersistentBufferHandle handle, int capacity)
    {
        internal PersistentBufferHandle handle { get; } = handle;
        internal int capacity { get; } = capacity;
        internal int writeOffset { get; set; }
        internal bool usedThisFrame { get; set; }
        internal ulong lastUsedFrame { get; set; }
    }
}
