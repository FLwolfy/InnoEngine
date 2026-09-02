using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using Inno.Assets;

namespace Inno.Plugins.Authoring;

/// <summary>
/// Publishes the host-validated active local Plugin snapshot.
/// </summary>
internal sealed class PluginCatalog
{
    private readonly object m_sync = new();
    private Snapshot s_current = Snapshot.empty;
    private PluginScanResult s_discovery = new([], []);
    private long s_revision;

    /// <summary>
    /// Occurs after installed discovery or active Plugin state changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets dependency-ordered active Plugins.
    /// </summary>
    public IReadOnlyList<PluginCandidate> activePlugins
    {
        get
        {
            lock (m_sync)
                return s_current.ordered;
        }
    }

    /// <summary>
    /// Gets the monotonic identity of the active validated Plugin generation.
    /// </summary>
    public long revision
    {
        get
        {
            lock (m_sync)
                return s_revision;
        }
    }

    /// <summary>
    /// Gets the latest validated installed Plugin discovery and isolated diagnostics.
    /// </summary>
    public PluginScanResult discovery
    {
        get
        {
            lock (m_sync)
                return s_discovery;
        }
    }

    /// <summary>
    /// Tries to resolve the active Plugin that owns an asset source.
    /// </summary>
    /// <param name="source">
    /// Asset source identity.
    /// </param>
    /// <param name="plugin">
    /// Receives the active Plugin candidate.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source belongs to an active Plugin.
    /// </returns>
    public bool TryGet(AssetSourceId source, out PluginCandidate? plugin)
    {
        lock (m_sync)
            return s_current.bySource.TryGetValue(source, out plugin);
    }

    /// <summary>
    /// Atomically publishes a fully validated Plugin scan.
    /// </summary>
    /// <param name="result">
    /// Validated Plugin source scan result.
    /// </param>
    public void Activate(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PluginCandidate[] ordered = PluginSourceService.GetActivatableCandidates(result).ToArray();
        var bySource = ordered.ToFrozenDictionary(
            static candidate => candidate.sourceMount.id,
            static candidate => candidate);
        Action? changed;
        lock (m_sync)
        {
            s_discovery = result;
            s_current = new Snapshot(ordered, bySource);
            s_revision++;
            changed = Changed;
        }
        InvokeObservers(changed);
    }

    /// <summary>
    /// Publishes discovery diagnostics without changing the active mount generation.
    /// </summary>
    /// <param name="result">
    /// Latest validated installed Plugin source scan.
    /// </param>
    public void PublishDiscovery(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Action? changed;
        lock (m_sync)
        {
            s_discovery = result;
            changed = Changed;
        }
        InvokeObservers(changed);
    }

    /// <summary>
    /// Clears the active Plugin snapshot.
    /// </summary>
    public void Shutdown()
    {
        lock (m_sync)
        {
            s_current = Snapshot.empty;
            s_discovery = new PluginScanResult([], []);
            s_revision++;
            Changed = null;
        }
    }

    private static void InvokeObservers(Action? handlers)
    {
        if (handlers is null)
            return;
        List<Exception>? failures = null;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Plugin catalog observers failed after publication.",
                failures);
        }
    }

    private sealed record Snapshot(
        IReadOnlyList<PluginCandidate> ordered,
        FrozenDictionary<AssetSourceId, PluginCandidate> bySource)
    {
        internal static Snapshot empty { get; } = new(
            [],
            new Dictionary<AssetSourceId, PluginCandidate>().ToFrozenDictionary());
    }
}
