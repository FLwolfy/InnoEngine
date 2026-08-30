using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using Inno.Assets.Core;

namespace Inno.Assets.Plugins;

/// <summary>Publishes the host-validated active local Plugin snapshot.</summary>
public static class PluginCatalog
{
    private static readonly object S_SYNC = new();
    private static Snapshot s_current = Snapshot.empty;
    private static PluginScanResult s_discovery = new([], []);

    /// <summary>Occurs after installed discovery or active Plugin state changes.</summary>
    public static event Action? Changed;

    /// <summary>Gets dependency-ordered active Plugins.</summary>
    public static IReadOnlyList<PluginCandidate> activePlugins
    {
        get
        {
            lock (S_SYNC)
                return s_current.ordered;
        }
    }

    /// <summary>Gets the latest validated installed Plugin discovery and isolated diagnostics.</summary>
    public static PluginScanResult discovery
    {
        get
        {
            lock (S_SYNC)
                return s_discovery;
        }
    }

    /// <summary>Tries to resolve the active Plugin that owns an asset source.</summary>
    /// <param name="source">Asset source identity.</param>
    /// <param name="plugin">Receives the active Plugin candidate.</param>
    /// <returns><see langword="true"/> when the source belongs to an active Plugin.</returns>
    public static bool TryGet(AssetSourceId source, out PluginCandidate? plugin)
    {
        lock (S_SYNC)
            return s_current.bySource.TryGetValue(source, out plugin);
    }

    /// <summary>Atomically publishes a fully validated Plugin scan.</summary>
    /// <param name="result">Validated Plugin source scan result.</param>
    public static void Activate(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PluginCandidate[] ordered = PluginSourceService.GetActivatableCandidates(result).ToArray();
        var bySource = ordered.ToFrozenDictionary(
            static candidate => candidate.sourceMount.id,
            static candidate => candidate);
        Action? changed;
        lock (S_SYNC)
        {
            s_discovery = result;
            s_current = new Snapshot(ordered, bySource);
            changed = Changed;
        }
        changed?.Invoke();
    }

    /// <summary>Publishes discovery diagnostics without changing the active mount generation.</summary>
    /// <param name="result">Latest validated installed Plugin source scan.</param>
    public static void PublishDiscovery(PluginScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Action? changed;
        lock (S_SYNC)
        {
            s_discovery = result;
            changed = Changed;
        }
        changed?.Invoke();
    }

    /// <summary>Clears the active Plugin snapshot.</summary>
    public static void Shutdown()
    {
        lock (S_SYNC)
        {
            s_current = Snapshot.empty;
            s_discovery = new PluginScanResult([], []);
            Changed = null;
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
