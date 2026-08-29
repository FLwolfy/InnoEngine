using System;
using System.Collections.Generic;
using Inno.Rendering.Core;

namespace Inno.Rendering.Runtime;

/// <summary>Owns persistent offscreen targets without exposing backend-native handles.</summary>
public sealed class RenderTargetRegistry : IDisposable
{
    private readonly IRenderDevice m_device;
    private readonly Dictionary<RenderTexture, TargetEntry> m_targets = [];
    private readonly HashSet<RenderTexture> m_pendingReleases = [];
    private bool m_disposed;

    /// <summary>Creates a target registry for one device generation.</summary>
    /// <param name="device">Device that owns persistent target resources.</param>
    public RenderTargetRegistry(IRenderDevice device)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>Imports or creates one target in the current frame graph.</summary>
    /// <param name="graph">Current frame graph builder.</param>
    /// <param name="target">Persistent target description.</param>
    /// <returns>A graph-scoped handle for the current target resource.</returns>
    public RenderTextureHandle Import(RenderGraphBuilder graph, RenderTexture target)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(target);
        m_pendingReleases.Remove(target);

        if (!m_targets.TryGetValue(target, out TargetEntry? entry)
            || entry.revision != target.contentRevision
            || !entry.descriptor.Equals(target.descriptor))
        {
            PersistentTextureHandle next = m_device.CreateTexture(target.descriptor, target.name);
            if (entry is not null)
                m_device.DestroyTexture(entry.handle);
            entry = new TargetEntry(target.contentRevision, target.descriptor, next);
            m_targets[target] = entry;
        }

        return graph.ImportTexture(target.name, entry.handle, entry.descriptor);
    }

    /// <summary>Tries to get the current opaque device texture for UI presentation.</summary>
    /// <param name="target">Persistent target description.</param>
    /// <param name="texture">Receives a backend-neutral persistent handle.</param>
    /// <returns><see langword="true"/> when the target has completed allocation.</returns>
    public bool TryGetTexture(RenderTexture target, out PersistentTextureHandle texture)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (m_targets.TryGetValue(target, out TargetEntry? entry))
        {
            texture = entry.handle;
            return true;
        }

        texture = default;
        return false;
    }

    /// <summary>Queues one target for destruction at the next frame safety point.</summary>
    /// <param name="target">Target no longer used by request producers.</param>
    public void Release(RenderTexture target)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        m_pendingReleases.Add(target);
    }

    /// <summary>Applies queued target releases at a frame safety point.</summary>
    public void PrepareFrame()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        foreach (RenderTexture target in m_pendingReleases)
        {
            if (m_targets.Remove(target, out TargetEntry? entry))
                m_device.DestroyTexture(entry.handle);
        }
        m_pendingReleases.Clear();
    }

    /// <summary>Queues all owned resources for device-safe destruction.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        foreach (TargetEntry entry in m_targets.Values)
            m_device.DestroyTexture(entry.handle);
        m_targets.Clear();
        m_pendingReleases.Clear();
        m_disposed = true;
    }

    private sealed record TargetEntry(
        long revision,
        RenderTextureDescriptor descriptor,
        PersistentTextureHandle handle);
}
