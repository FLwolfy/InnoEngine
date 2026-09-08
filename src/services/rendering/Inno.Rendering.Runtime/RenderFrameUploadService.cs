using System;
using Inno.Core.Execution;
using System.Collections.Generic;
using System.Linq;

using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderFrameUploadService : RenderFrameUploadProvider, IRenderFrameUploadService, IDisposable
{
    private const int C_MINIMUM_PAGE_ELEMENTS = 256;
    private const ulong C_UNUSED_FRAME_LIMIT = 240;

    private readonly IRenderDevice m_device;
    private readonly RenderResourceLimits m_limits;
    private RenderRetirementQueue? m_sweep;
    private int m_pageCount;
    private long m_residentBytes;
    private long m_frameBytes;
    private long m_peakResidentBytes;
    private long m_rejected;
    private readonly Dictionary<UploadPageKey, UploadPool> m_pools = [];
    private ulong m_frameIndex;
    private bool m_frameOpen;
    private bool m_disposed;
    private RenderRetirementQueue? m_retirement;

    internal RenderFrameUploadService(IRenderDevice device, RenderResourceLimits limits)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_limits = limits;
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
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
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

        DrainSweep();
        if (data.Length > m_limits.uploadBytesPerFrame - m_frameBytes)
        {
            m_rejected++;
            throw new InvalidOperationException("The per-frame upload byte budget has been reached.");
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
            long bytes = checked((long)capacity * descriptor.elementStride);
            if (m_pageCount >= m_limits.uploadPages || bytes > m_limits.uploadResidentBytes - m_residentBytes)
            {
                m_rejected++;
                throw new InvalidOperationException("The resident upload page or byte budget has been reached.");
            }
            RenderBufferUsage usage = descriptor.usage | RenderBufferUsage.Dynamic;
            var bufferDescriptor = new PersistentBufferDescriptor(
                new RenderBufferDescriptor(capacity, descriptor.elementStride, usage),
                descriptor.vertexLayout,
                descriptor.indexFormat);
            PersistentBufferHandle handle = m_device.CreateBuffer(
                bufferDescriptor,
                ReadOnlySpan<byte>.Empty,
                $"{name}/FrameUpload[{pool.pages.Count}]");
            page = new UploadPage(handle, capacity, bytes);
            pool.pages.Add(page);
            m_pools[key] = pool;
            m_pageCount++;
            m_residentBytes += bytes;
            m_peakResidentBytes = Math.Max(m_peakResidentBytes, m_residentBytes);
        }

        int firstElement = requiresDedicatedPage ? 0 : page.writeOffset;
        m_device.UpdateBuffer(page.handle, data.Span, firstElement);
        m_frameBytes += data.Length;
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
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
        DrainSweep();
        m_frameBytes = 0;
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
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
        if (m_frameIndex < C_UNUSED_FRAME_LIMIT)
            return;
        DrainSweep();
        ulong oldest = m_frameIndex - C_UNUSED_FRAME_LIMIT;
        foreach (UploadPageKey key in m_pools.Keys.ToArray())
        {
            UploadPool pool = m_pools[key];
            foreach (UploadPage page in pool.pages
                         .Where(candidate => candidate.lastUsedFrame < oldest)
                         .ToArray())
            {
                m_sweep ??= new RenderRetirementQueue();
                m_sweep.Add(() => m_device.DestroyBuffer(page.handle));
                m_sweep.Add(() => { m_residentBytes -= page.bytes; m_pageCount--; });
                pool.pages.Remove(page);
            }
            if (pool.pages.Count == 0)
                m_pools.Remove(key);
        }
        DrainSweep();
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_frameOpen = false;
        if (m_retirement is null)
        {
            m_retirement = new RenderRetirementQueue();
            if (m_sweep is not null) m_retirement.Add(m_sweep.Dispose);
            foreach (UploadPage page in m_pools.Values.SelectMany(static pool => pool.pages))
                m_retirement.Add(() => m_device.DestroyBuffer(page.handle));
        }
        try { m_retirement.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_pools.Clear();
            m_residentBytes = 0;
            m_pageCount = 0;
            m_disposed = true;
            throw;
        }
        m_pools.Clear();
        m_residentBytes = 0;
        m_pageCount = 0;
        m_disposed = true;
    }

    internal int pageCount => m_pageCount;
    internal long residentBytes => m_residentBytes;
    internal long peakResidentBytes => m_peakResidentBytes;
    internal long frameBytes => m_frameBytes;
    internal long rejectedCount => m_rejected;

    private void DrainSweep()
    {
        if (m_sweep is null) return;
        try { m_sweep.Dispose(); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch { m_sweep = null; throw; }
        m_sweep = null;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result = checked(result * 2);
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

    private sealed class UploadPage(PersistentBufferHandle handle, int capacity, long bytes)
    {
        internal PersistentBufferHandle handle { get; } = handle;
        internal int capacity { get; } = capacity;
        internal long bytes { get; } = bytes;
        internal int writeOffset { get; set; }
        internal bool usedThisFrame { get; set; }
        internal ulong lastUsedFrame { get; set; }
    }
}
