using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Execution;
using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderReadbackOwner(IRenderDevice device, int capacity) : IDisposable
{
    private readonly Dictionary<RenderTextureReadbackHandle, PendingReadback> m_pending = [];
    private RenderRetirementQueue? m_retirement;
    private bool m_disposed;
    private int m_peak;
    private long m_rejected;

    internal int count => m_pending.Count;
    internal int peakCount => m_peak;
    internal long rejectedCount => m_rejected;

    internal ValueTask<RenderTextureReadbackResult> Read(
        PersistentTextureHandle texture, int mipLevel, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
        cancellation.ThrowIfCancellationRequested();
        if (!device.capabilities.Supports(GraphicsCapability.TextureReadback))
            throw new NotSupportedException("The active backend does not support texture readback.");
        if (m_pending.Count >= capacity)
        {
            m_rejected++;
            throw new InvalidOperationException($"Readback capacity {capacity} has been reached.");
        }
        var pending = new PendingReadback(cancellation);
        RenderTextureReadbackHandle handle = device.BeginTextureReadback(texture, mipLevel);
        m_pending.Add(handle, pending);
        m_peak = Math.Max(m_peak, m_pending.Count);
        return new ValueTask<RenderTextureReadbackResult>(pending.completion.Task);
    }

    internal void Update()
    {
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
        foreach ((RenderTextureReadbackHandle handle, PendingReadback pending) in m_pending.ToArray())
        {
            if (pending.failure is null && !pending.cancellation.IsCancellationRequested)
            {
                try
                {
                    if (!device.TryGetTextureReadback(handle, out RenderTextureReadbackResult? result)) continue;
                    m_pending.Remove(handle);
                    pending.completion.TrySetResult(result!);
                    continue;
                }
                catch (Exception unfinished) when (RetirementPendingException.Find(unfinished) is not null) { throw; }
                catch (Exception failure) { pending.failure = failure; }
            }
            pending.retirement ??= new RetirementBarrier("Texture readback cancellation");
            try
            {
                if (!pending.retirement.TryComplete(() => device.CancelTextureReadback(handle)))
                    throw new RetirementPendingException("Texture readback cancellation is still pending.");
            }
            catch (Exception unfinished) when (RetirementPendingException.Find(unfinished) is not null) { throw; }
            catch (Exception cleanup)
            {
                pending.failure = pending.failure is null ? cleanup : new AggregateException(pending.failure, cleanup);
            }
            m_pending.Remove(handle);
            if (pending.failure is not null) pending.completion.TrySetException(pending.failure);
            else pending.completion.TrySetCanceled(pending.cancellation);
        }
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed) return;
        if (m_retirement is null)
        {
            m_retirement = new RenderRetirementQueue();
            foreach ((RenderTextureReadbackHandle handle, PendingReadback pending) in m_pending)
            {
                m_retirement.Add(() => device.CancelTextureReadback(handle));
                m_retirement.Add(() => pending.completion.TrySetException(new ObjectDisposedException(nameof(RenderReadbackOwner))));
            }
        }
        try { m_retirement.Dispose(); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch
        {
            m_pending.Clear();
            m_disposed = true;
            throw;
        }
        m_pending.Clear();
        m_disposed = true;
    }

    private sealed class PendingReadback(CancellationToken cancellation)
    {
        internal Exception? failure { get; set; }
        internal RetirementBarrier? retirement { get; set; }
        internal CancellationToken cancellation { get; } = cancellation;
        internal TaskCompletionSource<RenderTextureReadbackResult> completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
