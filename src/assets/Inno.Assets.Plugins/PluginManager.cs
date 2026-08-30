using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Scripting;
using Inno.Core.Settings;

namespace Inno.Assets.Plugins;

/// <summary>
/// Owns automatic installed Plugin source discovery and atomically publishes Asset mounts, settings contributors,
/// and the active Plugin catalog on the Asset Database owner thread.
/// </summary>
public static class PluginManager
{
    private const long C_SCAN_INTERVAL_MILLISECONDS = 500;

    private static readonly object S_SYNC = new();
    private static PluginSourceService? s_sources;
    private static string s_pluginRoot = string.Empty;
    private static string s_directoryFingerprint = string.Empty;
    private static string s_activeFingerprint = string.Empty;
    private static long s_lastScanTimestamp;
    private static PendingActivation? s_pending;

    /// <summary>
    /// Occurs after a validated Plugin generation is staged for script compilation but before it becomes active.
    /// </summary>
    [ScriptingApiIgnore]
    public static event Action? ActivationCandidateChanged;

    /// <summary>Gets whether project Plugin management is initialized.</summary>
    [ScriptingApiIgnore]
    public static bool isInitialized
    {
        get
        {
            lock (S_SYNC)
                return s_sources is not null;
        }
    }

    /// <summary>Initializes automatic Plugin discovery around an already initialized common AssetManager.</summary>
    /// <param name="pluginRoot">Project Plugins directory.</param>
    /// <param name="libraryRoot">Project rebuildable Library directory.</param>
    /// <param name="initialScan">Scan already used to create the initial Asset mount generation.</param>
    [ScriptingApiIgnore]
    public static void Initialize(
        string pluginRoot,
        string libraryRoot,
        PluginScanResult initialScan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(initialScan);
        if (!AssetManager.isInitialized)
            throw new InvalidOperationException("PluginManager requires AssetManager to be initialized first.");
        lock (S_SYNC)
        {
            if (s_sources is not null)
                throw new InvalidOperationException("PluginManager is already initialized.");
            s_pluginRoot = Path.GetFullPath(pluginRoot);
            s_sources = new PluginSourceService(s_pluginRoot, libraryRoot);
            s_directoryFingerprint = ComputeDirectoryFingerprint(s_pluginRoot);
            s_lastScanTimestamp = Environment.TickCount64;
        }

        PluginScanResult activeScan = ActivateInitialCandidate(initialScan);
        lock (S_SYNC)
            s_activeFingerprint = ComputeActiveFingerprint(activeScan);
    }

    /// <summary>Polls the sibling Plugins directory and refreshes only after its source snapshot changes.</summary>
    [ScriptingApiIgnore]
    public static void Update()
    {
        PluginSourceService? sources;
        string pluginRoot;
        lock (S_SYNC)
        {
            sources = s_sources;
            pluginRoot = s_pluginRoot;
            if (sources is null || Environment.TickCount64 - s_lastScanTimestamp < C_SCAN_INTERVAL_MILLISECONDS)
                return;
            s_lastScanTimestamp = Environment.TickCount64;
        }

        string fingerprint = ComputeDirectoryFingerprint(pluginRoot);
        lock (S_SYNC)
        {
            if (string.Equals(fingerprint, s_directoryFingerprint, StringComparison.Ordinal))
                return;
            s_directoryFingerprint = fingerprint;
        }
        _ = Refresh();
    }

    /// <summary>Forces validation and attempts one atomic active Plugin generation replacement.</summary>
    /// <returns>True when the active Plugin mount generation changed.</returns>
    [ScriptingApiIgnore]
    public static bool Refresh()
    {
        PluginSourceService sources;
        lock (S_SYNC)
            sources = s_sources ?? throw new InvalidOperationException("PluginManager is not initialized.");
        PluginScanResult scan = sources.Scan();
        PluginCatalog.PublishDiscovery(scan);
        HashSet<string> activeSourcePaths = PluginCatalog.activePlugins
            .Select(static candidate => Path.GetFullPath(candidate.sourcePath))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (scan.diagnostics.Any(diagnostic =>
                (System.IO.File.Exists(diagnostic.sourcePath) || Directory.Exists(diagnostic.sourcePath))
                && activeSourcePaths.Contains(Path.GetFullPath(diagnostic.sourcePath))))
        {
            return false;
        }
        string activeFingerprint = ComputeActiveFingerprint(scan);
        lock (S_SYNC)
        {
            if (s_pending is not null)
                return false;
            if (string.Equals(activeFingerprint, s_activeFingerprint, StringComparison.Ordinal))
                return false;
        }

        PluginScanResult previous = CreateActiveScanSnapshot();
        bool requiresCodeReload = RequiresCodeReload(previous, scan);
        AssetSourceMount project = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        AssetSourceMountTransaction assets = AssetManager.PrepareSourceMounts(
            [project, .. PluginSourceService.GetActivatableMounts(scan)]);
        bool pendingAssigned = false;
        try
        {
            lock (S_SYNC)
            {
                if (s_pending is not null)
                    throw new InvalidOperationException("Another Plugin activation candidate is already pending.");
                s_pending = new PendingActivation(
                    previous,
                    scan,
                    assets,
                    s_activeFingerprint,
                    activeFingerprint);
                pendingAssigned = true;
            }
            if (!requiresCodeReload)
            {
                ActivatePending();
                ProjectSettingsManager.RebuildCurrent();
                CommitPending();
            }
            else
            {
                InvokeCandidateObservers();
            }
            return true;
        }
        catch
        {
            if (pendingAssigned)
            {
                RollbackPending();
                if (!requiresCodeReload && ProjectSettingsManager.isInitialized)
                    ProjectSettingsManager.RebuildCurrent();
            }
            else
                assets.Rollback();
            throw;
        }
    }

    /// <summary>Gets whether a source-mount candidate is waiting for successful script generation activation.</summary>
    [ScriptingApiIgnore]
    public static bool hasPendingActivation
    {
        get
        {
            lock (S_SYNC)
                return s_pending is not null;
        }
    }

    /// <summary>Gets the isolated Asset candidate used by the next Plugin script compilation.</summary>
    [ScriptingApiIgnore]
    public static AssetSourceMountTransaction? compilationAssets
    {
        get
        {
            lock (S_SYNC)
                return s_pending?.assets;
        }
    }

    /// <summary>Gets the Plugin candidates visible to the next script compilation.</summary>
    [ScriptingApiIgnore]
    public static IReadOnlyList<PluginCandidate> compilationPlugins
    {
        get
        {
            lock (S_SYNC)
            {
                return s_pending is null
                    ? PluginCatalog.activePlugins
                    : PluginSourceService.GetActivatableCandidates(s_pending.candidateScan).ToArray();
            }
        }
    }

    /// <summary>Resolves a Plugin owned by the active or pending script-compilation generation.</summary>
    /// <param name="source">Plugin source mount ID.</param>
    /// <param name="plugin">Resolved Plugin candidate.</param>
    /// <returns>True when the compilation generation owns the source.</returns>
    [ScriptingApiIgnore]
    public static bool TryGetCompilationPlugin(
        AssetSourceId source,
        out PluginCandidate? plugin)
    {
        plugin = compilationPlugins.FirstOrDefault(candidate => candidate.sourceMount.id == source);
        return plugin is not null;
    }

    /// <summary>
    /// Provisionally publishes the pending generation at a caller-controlled assembly and frame safety point.
    /// </summary>
    [ScriptingApiIgnore]
    public static void ActivatePending()
    {
        PendingActivation? pending;
        lock (S_SYNC)
            pending = s_pending;
        if (pending is null || pending.isActivated)
            return;
        try
        {
            pending.assets.Activate();
            pending.isActivated = true;
            PluginCatalog.Activate(pending.candidateScan);
            PublishContributors(pending.candidateScan);
        }
        catch
        {
            RollbackPending();
            throw;
        }
    }

    /// <summary>Commits the pending mount generation after scripts, assets, settings, and registries activate.</summary>
    [ScriptingApiIgnore]
    public static void CommitPending()
    {
        PendingActivation? pending;
        lock (S_SYNC)
        {
            pending = s_pending;
            if (pending is null)
                return;
            if (!pending.isActivated)
                throw new InvalidOperationException("A Plugin candidate must be activated before commit.");
        }
        pending.assets.Complete();
        lock (S_SYNC)
        {
            if (!ReferenceEquals(s_pending, pending))
                throw new InvalidOperationException("The pending Plugin candidate changed during commit.");
            s_activeFingerprint = pending.candidateFingerprint;
            s_pending = null;
        }
    }

    /// <summary>Restores the complete last-good mount, catalog, and settings contributor generation.</summary>
    [ScriptingApiIgnore]
    public static void RollbackPending()
    {
        PendingActivation? pending;
        lock (S_SYNC)
        {
            pending = s_pending;
            s_pending = null;
        }
        if (pending is null)
            return;
        pending.assets.Rollback();
        if (pending.isActivated)
        {
            PluginCatalog.Activate(pending.previousScan);
            PluginCatalog.PublishDiscovery(pending.candidateScan);
            PublishContributors(pending.previousScan);
        }
        lock (S_SYNC)
            s_activeFingerprint = pending.previousFingerprint;
    }

    /// <summary>Stops automatic discovery without deleting Plugin sources or rebuildable extraction caches.</summary>
    [ScriptingApiIgnore]
    public static void Shutdown()
    {
        RollbackPending();
        lock (S_SYNC)
        {
            s_sources = null;
            s_pluginRoot = string.Empty;
            s_directoryFingerprint = string.Empty;
            s_activeFingerprint = string.Empty;
            s_lastScanTimestamp = 0;
            ActivationCandidateChanged = null;
        }
    }

    private static void PublishContributors(PluginScanResult scan)
        => ProjectSettingsManager.SetContributors(
            PluginSourceService.GetActivatableCandidates(scan)
                .Select(static candidate => new ProjectSettingsContributor(
                    candidate.manifest.pluginId,
                    candidate.manifest.dependencies,
                    candidate.manifest.overrides,
                    candidate.manifest.settingContributions))
                .ToArray());

    private static void InvokeCandidateObservers()
    {
        Action? handlers = ActivationCandidateChanged;
        if (handlers is null)
            return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // Candidate observers cannot partially activate or reject a validated Plugin generation.
            }
        }
    }

    private static PluginScanResult ActivateInitialCandidate(PluginScanResult initialScan)
    {
        AssetSourceMount project = AssetManager.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        try
        {
            AssetManager.ReplaceSourceMounts(
                [project, .. PluginSourceService.GetActivatableMounts(initialScan)]);
            PluginCatalog.Activate(initialScan);
            PublishContributors(initialScan);
            ProjectSettingsManager.RebuildCurrent(allowUnresolvedContributions: true);
            return initialScan;
        }
        catch (Exception candidateFailure)
        {
            var rollbackFailures = new List<Exception>();
            TryInitialRollback(
                () => AssetManager.ReplaceSourceMounts([project]),
                "asset mount rollback",
                rollbackFailures);
            var empty = new PluginScanResult([], []);
            TryInitialRollback(
                () => PluginCatalog.Activate(empty),
                "Plugin catalog rollback",
                rollbackFailures);
            TryInitialRollback(
                () =>
                {
                    PublishContributors(empty);
                    ProjectSettingsManager.RebuildCurrent(allowUnresolvedContributions: true);
                },
                "project settings rollback",
                rollbackFailures);
            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(
                    "Initial Plugin activation failed and the host-only generation could not be restored.",
                    [candidateFailure, .. rollbackFailures]);
            }

            var discovery = new PluginScanResult(
                initialScan.candidates,
                [
                    .. initialScan.diagnostics,
                    new PluginDiagnostic(
                        s_pluginRoot,
                        $"Initial Plugin candidate activation failed and was isolated: {candidateFailure.Message}")
                ]);
            PluginCatalog.PublishDiscovery(discovery);
            return empty;
        }
    }

    private static void TryInitialRollback(
        Action rollback,
        string stage,
        ICollection<Exception> failures)
    {
        try
        {
            rollback();
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException($"Initial Plugin {stage} failed.", exception));
        }
    }

    private static PluginScanResult CreateActiveScanSnapshot()
        => new(PluginCatalog.activePlugins.ToArray(), []);

    private static bool RequiresCodeReload(PluginScanResult previous, PluginScanResult candidate)
    {
        Dictionary<string, string> oldCode = PluginSourceService.GetActivatableCandidates(previous)
            .Where(static plugin => plugin.containsCode)
            .ToDictionary(static plugin => plugin.manifest.pluginId, static plugin => plugin.contentHash, StringComparer.Ordinal);
        Dictionary<string, string> newCode = PluginSourceService.GetActivatableCandidates(candidate)
            .Where(static plugin => plugin.containsCode)
            .ToDictionary(static plugin => plugin.manifest.pluginId, static plugin => plugin.contentHash, StringComparer.Ordinal);
        return oldCode.Count != newCode.Count
            || oldCode.Any(pair => !newCode.TryGetValue(pair.Key, out string? hash)
                || !string.Equals(pair.Value, hash, StringComparison.Ordinal));
    }

    private static string ComputeActiveFingerprint(PluginScanResult scan)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (PluginCandidate candidate in PluginSourceService.GetActivatableCandidates(scan))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(candidate.manifest.pluginId));
            hash.AppendData(Encoding.UTF8.GetBytes(candidate.contentHash));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeDirectoryFingerprint(string pluginRoot)
    {
        Directory.CreateDirectory(pluginRoot);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var paths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(pluginRoot);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                paths.Add(path);
                FileAttributes attributes = System.IO.File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0
                    && (attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(path);
                }
            }
        }
        foreach (string path in paths.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            FileAttributes attributes = System.IO.File.GetAttributes(path);
            string relative = Path.GetRelativePath(pluginRoot, path).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(BitConverter.GetBytes((int)attributes));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                DirectoryInfo directory = new(path);
                hash.AppendData(BitConverter.GetBytes(directory.LastWriteTimeUtc.Ticks));
                continue;
            }
            FileInfo file = new(path);
            hash.AppendData(BitConverter.GetBytes(file.Length));
            hash.AppendData(BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class PendingActivation(
        PluginScanResult previousScan,
        PluginScanResult candidateScan,
        AssetSourceMountTransaction assets,
        string previousFingerprint,
        string candidateFingerprint)
    {
        internal PluginScanResult previousScan { get; } = previousScan;
        internal PluginScanResult candidateScan { get; } = candidateScan;
        internal AssetSourceMountTransaction assets { get; } = assets;
        internal string previousFingerprint { get; } = previousFingerprint;
        internal string candidateFingerprint { get; } = candidateFingerprint;
        internal bool isActivated { get; set; }
    }
}
