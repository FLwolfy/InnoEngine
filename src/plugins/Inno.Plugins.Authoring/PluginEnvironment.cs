using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Scripting.Api;
using Inno.Core.Serialization;
using Inno.Core.Settings;

namespace Inno.Plugins.Authoring;

/// <summary>
/// Owns automatic installed Plugin source discovery and atomically publishes Asset mounts, settings contributors,
/// and the active Plugin catalog on the Asset Database owner thread.
/// </summary>
public sealed class PluginEnvironment : IDisposable
{
    private const long C_CHANGE_DEBOUNCE_MILLISECONDS = 150;
    private const long C_RECONCILIATION_INTERVAL_MILLISECONDS = 30_000;

    private readonly object m_sync = new();
    private readonly AssetPipeline m_assets;
    private readonly PluginCatalog m_catalog = new();
    private readonly ProjectSettingsStore m_settings;
    private PluginSourceService? m_sources;
    private FileSystemWatcher? m_watcher;
    private string m_pluginRoot = string.Empty;
    private string m_reconciledFingerprint = string.Empty;
    private string m_activeFingerprint = string.Empty;
    private long m_lastChangeTimestamp;
    private long m_lastReconciliationTimestamp;
    private bool m_refreshRequested;
    private Task<string>? m_reconciliation;
    private PendingActivation? m_pending;

    /// <summary>
    /// Occurs after a validated Plugin generation is staged for script compilation but before it becomes active.
    /// </summary>
    [ScriptingApiIgnore]
    public event Action? ActivationCandidateChanged;

    /// <summary>
    /// Gets whether project Plugin management is initialized.
    /// </summary>
    [ScriptingApiIgnore]
    public bool isInitialized
    {
        get
        {
            lock (m_sync)
                return m_sources is not null;
        }
    }

    /// <summary>
    /// Gets the dependency-ordered active Plugin generation.
    /// </summary>
    public IReadOnlyList<PluginCandidate> activePlugins => m_catalog.activePlugins;

    /// <summary>
    /// Gets the monotonic identity of the active Plugin generation.
    /// </summary>
    public long revision => m_catalog.revision;

    /// <summary>
    /// Gets the latest installed Plugin discovery and isolated diagnostics.
    /// </summary>
    public PluginScanResult discovery => m_catalog.discovery;

    /// <summary>
    /// Occurs after discovery diagnostics or the active Plugin generation changes.
    /// </summary>
    public event Action? Changed
    {
        add => m_catalog.Changed += value;
        remove => m_catalog.Changed -= value;
    }

    /// <summary>
    /// Creates automatic Plugin discovery around one asset pipeline and project settings store.
    /// </summary>
    /// <param name="assets">
    /// The authoring asset pipeline that owns Plugin source mounts.
    /// </param>
    /// <param name="settings">
    /// The project settings store that composes active Plugin contributions.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used to validate installed Plugin manifests.
    /// </param>
    /// <param name="pluginRoot">
    /// Project Plugins directory.
    /// </param>
    /// <param name="libraryRoot">
    /// Project rebuildable Library directory.
    /// </param>
    /// <param name="initialScan">
    /// Scan already used to create the initial Asset mount generation.
    /// </param>
    [ScriptingApiIgnore]
    public PluginEnvironment(
        AssetPipeline assets,
        ProjectSettingsStore settings,
        SerializationRegistry serialization,
        string pluginRoot,
        string libraryRoot,
        PluginScanResult initialScan)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(initialScan);
        if (!assets.isInitialized)
            throw new InvalidOperationException("PluginEnvironment requires AssetPipeline to be initialized first.");
        m_assets = assets;
        m_settings = settings;
        lock (m_sync)
        {
            m_pluginRoot = Path.GetFullPath(pluginRoot);
            m_sources = new PluginSourceService(serialization, m_pluginRoot, libraryRoot);
            m_reconciledFingerprint = ComputeInstallationFingerprint(m_pluginRoot);
            m_lastReconciliationTimestamp = Environment.TickCount64;
            m_watcher = CreateWatcher(m_pluginRoot);
        }

        PluginScanResult activeScan = ActivateInitialCandidate(initialScan);
        lock (m_sync)
            m_activeFingerprint = ComputeActiveFingerprint(activeScan);
    }

    /// <summary>
    /// Processes debounced Plugin source notifications and completed background reconciliation.
    /// </summary>
    [ScriptingApiIgnore]
    public void Update()
    {
        CompleteReconciliation();
        bool refresh;
        lock (m_sync)
        {
            if (m_sources is null)
                return;
            long now = Environment.TickCount64;
            StartReconciliationLocked(now);
            refresh = m_refreshRequested
                && now - m_lastChangeTimestamp >= C_CHANGE_DEBOUNCE_MILLISECONDS;
            if (!refresh)
                return;
            m_refreshRequested = false;
        }
        _ = Refresh();
    }

    /// <summary>
    /// Forces validation and prepares or commits one atomic Plugin availability generation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a different mount generation was committed immediately or staged for code reload;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    [ScriptingApiIgnore]
    public bool Refresh()
    {
        PluginSourceService sources;
        lock (m_sync)
            sources = m_sources ?? throw new InvalidOperationException("PluginEnvironment is not initialized.");
        PluginScanResult scan = sources.Scan();
        m_catalog.PublishDiscovery(scan);
        string activeFingerprint = ComputeActiveFingerprint(scan);
        lock (m_sync)
        {
            if (m_pending is not null)
                return false;
            if (string.Equals(activeFingerprint, m_activeFingerprint, StringComparison.Ordinal))
                return false;
        }

        PluginScanResult previous = CreateActiveScanSnapshot();
        AssetSourceMount project = m_assets.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        AssetSourceMountTransaction assets;
        try
        {
            assets = m_assets.PrepareSourceMounts(
                [project, .. PluginSourceService.GetActivatableMounts(scan)]);
        }
        catch (Exception exception)
        {
            scan = new PluginScanResult(
                [],
                [
                    .. scan.diagnostics,
                    new PluginDiagnostic(
                        m_pluginRoot,
                        $"Plugin Asset candidate validation failed; the installed Plugin generation is unavailable: " +
                        exception.Message)
                ]);
            m_catalog.PublishDiscovery(scan);
            activeFingerprint = ComputeActiveFingerprint(scan);
            lock (m_sync)
            {
                if (m_pending is not null)
                    return false;
                if (string.Equals(activeFingerprint, m_activeFingerprint, StringComparison.Ordinal))
                    return false;
            }
            assets = m_assets.PrepareSourceMounts([project]);
        }
        bool requiresCodeReload = RequiresCodeReload(previous, scan);
        bool pendingAssigned = false;
        try
        {
            lock (m_sync)
            {
                if (m_pending is not null)
                    throw new InvalidOperationException("Another Plugin activation candidate is already pending.");
                m_pending = new PendingActivation(
                    previous,
                    scan,
                    assets,
                    m_activeFingerprint,
                    activeFingerprint);
                pendingAssigned = true;
            }
            if (!requiresCodeReload)
            {
                ActivatePending();
                m_settings.RebuildCurrent();
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
                if (!requiresCodeReload && m_settings.isInitialized)
                    m_settings.RebuildCurrent();
            }
            else
                assets.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Gets whether a source-mount candidate is waiting for successful script generation activation.
    /// </summary>
    [ScriptingApiIgnore]
    public bool hasPendingActivation
    {
        get
        {
            lock (m_sync)
                return m_pending is not null;
        }
    }

    /// <summary>
    /// Gets the isolated Asset candidate used by the next Plugin script compilation.
    /// </summary>
    [ScriptingApiIgnore]
    public AssetSourceMountTransaction? compilationAssets
    {
        get
        {
            lock (m_sync)
                return m_pending?.assets;
        }
    }

    /// <summary>
    /// Gets the Plugin candidates visible to the next script compilation.
    /// </summary>
    [ScriptingApiIgnore]
    public IReadOnlyList<PluginCandidate> compilationPlugins
    {
        get
        {
            lock (m_sync)
            {
                return m_pending is null
                    ? m_catalog.activePlugins
                    : PluginSourceService.GetActivatableCandidates(m_pending.candidateScan).ToArray();
            }
        }
    }

    /// <summary>
    /// Resolves a Plugin owned by the active or pending script-compilation generation.
    /// </summary>
    /// <param name="source">
    /// Plugin source mount ID.
    /// </param>
    /// <param name="plugin">
    /// Resolved Plugin candidate.
    /// </param>
    /// <returns>
    /// True when the compilation generation owns the source.
    /// </returns>
    [ScriptingApiIgnore]
    public bool TryGetCompilationPlugin(
        AssetSourceId source,
        out PluginCandidate? plugin)
    {
        plugin = compilationPlugins.FirstOrDefault(candidate => candidate.sourceMount.id == source);
        return plugin is not null;
    }

    /// <summary>
    /// Tries to resolve the active Plugin that owns an asset source.
    /// </summary>
    /// <param name="source">
    /// The isolated asset source identity.
    /// </param>
    /// <param name="plugin">
    /// Receives the active Plugin candidate when the source is owned by one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an active Plugin owns the source.
    /// </returns>
    public bool TryGet(AssetSourceId source, out PluginCandidate? plugin)
        => m_catalog.TryGet(source, out plugin);

    /// <summary>
    /// Provisionally publishes the pending generation at a caller-controlled assembly and frame safety point.
    /// </summary>
    [ScriptingApiIgnore]
    public void ActivatePending()
    {
        PendingActivation? pending;
        lock (m_sync)
            pending = m_pending;
        if (pending is null || pending.isActivated)
            return;
        try
        {
            pending.assets.Activate();
            pending.isActivated = true;
            m_catalog.Activate(pending.candidateScan);
            PublishContributors(pending.candidateScan);
        }
        catch
        {
            RollbackPending();
            throw;
        }
    }

    /// <summary>
    /// Commits the pending mount generation after scripts, assets, settings, and registries activate.
    /// </summary>
    [ScriptingApiIgnore]
    public void CommitPending()
    {
        PendingActivation? pending;
        lock (m_sync)
        {
            pending = m_pending;
            if (pending is null)
                return;
            if (!pending.isActivated)
                throw new InvalidOperationException("A Plugin candidate must be activated before commit.");
        }
        pending.assets.Complete();
        lock (m_sync)
        {
            if (!ReferenceEquals(m_pending, pending))
                throw new InvalidOperationException("The pending Plugin candidate changed during commit.");
            m_activeFingerprint = pending.candidateFingerprint;
            m_pending = null;
        }
    }

    /// <summary>
    /// Restores the complete last-good mount, catalog, and settings contributor generation.
    /// </summary>
    [ScriptingApiIgnore]
    public void RollbackPending()
    {
        PendingActivation? pending;
        lock (m_sync)
        {
            pending = m_pending;
            m_pending = null;
        }
        if (pending is null)
            return;
        pending.assets.Rollback();
        if (pending.isActivated)
        {
            m_catalog.Activate(pending.previousScan);
            m_catalog.PublishDiscovery(pending.candidateScan);
            PublishContributors(pending.previousScan);
        }
        lock (m_sync)
            m_activeFingerprint = pending.previousFingerprint;
    }

    /// <summary>
    /// Stops automatic discovery without deleting Plugin sources or rebuildable extraction caches.
    /// </summary>
    [ScriptingApiIgnore]
    public void Dispose()
    {
        RollbackPending();
        FileSystemWatcher? watcher;
        lock (m_sync)
        {
            watcher = m_watcher;
            m_watcher = null;
            m_sources = null;
            m_pluginRoot = string.Empty;
            m_reconciledFingerprint = string.Empty;
            m_activeFingerprint = string.Empty;
            m_lastChangeTimestamp = 0;
            m_lastReconciliationTimestamp = 0;
            m_refreshRequested = false;
            m_reconciliation = null;
            ActivationCandidateChanged = null;
        }
        watcher?.Dispose();
        m_catalog.Shutdown();
        GC.SuppressFinalize(this);
    }

    private FileSystemWatcher CreateWatcher(string pluginRoot)
    {
        var watcher = new FileSystemWatcher(pluginRoot)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
            Filter = "*"
        };
        watcher.Changed += OnPluginSourceChanged;
        watcher.Created += OnPluginSourceChanged;
        watcher.Deleted += OnPluginSourceChanged;
        watcher.Renamed += OnPluginSourceChanged;
        watcher.Error += OnPluginSourceWatcherError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnPluginSourceChanged(object sender, FileSystemEventArgs arguments)
    {
        _ = sender;
        _ = arguments;
        lock (m_sync)
        {
            if (m_sources is null)
                return;
            m_refreshRequested = true;
            m_lastChangeTimestamp = Environment.TickCount64;
        }
    }

    private void OnPluginSourceWatcherError(object sender, ErrorEventArgs arguments)
    {
        _ = sender;
        _ = arguments;
        lock (m_sync)
        {
            if (m_sources is null)
                return;
            m_refreshRequested = true;
            m_lastChangeTimestamp = Environment.TickCount64 - C_CHANGE_DEBOUNCE_MILLISECONDS;
            m_lastReconciliationTimestamp = 0;
        }
    }

    private void StartReconciliationLocked(long now)
    {
        if (m_reconciliation is not null
            || now - m_lastReconciliationTimestamp < C_RECONCILIATION_INTERVAL_MILLISECONDS)
        {
            return;
        }
        string pluginRoot = m_pluginRoot;
        m_lastReconciliationTimestamp = now;
        m_reconciliation = Task.Run(() => ComputeInstallationFingerprint(pluginRoot));
    }

    private void CompleteReconciliation()
    {
        Task<string>? reconciliation;
        lock (m_sync)
        {
            reconciliation = m_reconciliation;
            if (reconciliation is null || !reconciliation.IsCompleted)
                return;
            m_reconciliation = null;
        }

        string fingerprint;
        try
        {
            fingerprint = reconciliation.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lock (m_sync)
            {
                if (m_sources is null)
                    return;
                m_refreshRequested = true;
                m_lastChangeTimestamp = Environment.TickCount64 - C_CHANGE_DEBOUNCE_MILLISECONDS;
            }
            return;
        }

        lock (m_sync)
        {
            if (m_sources is null
                || string.Equals(fingerprint, m_reconciledFingerprint, StringComparison.Ordinal))
            {
                return;
            }
            m_reconciledFingerprint = fingerprint;
            m_refreshRequested = true;
            m_lastChangeTimestamp = Environment.TickCount64 - C_CHANGE_DEBOUNCE_MILLISECONDS;
        }
    }

    private void PublishContributors(PluginScanResult scan)
        => m_settings.SetContributors(
            PluginSourceService.GetActivatableCandidates(scan)
                .Select(static candidate => new ProjectSettingsContributor(
                    candidate.manifest.pluginId,
                    candidate.manifest.dependencies,
                    candidate.manifest.overrides,
                    candidate.manifest.settingContributions))
                .ToArray());

    private void InvokeCandidateObservers()
    {
        Action? handlers = ActivationCandidateChanged;
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
                "One or more Plugin candidate observers rejected the staged generation.",
                failures);
        }
    }

    private PluginScanResult ActivateInitialCandidate(PluginScanResult initialScan)
    {
        AssetSourceMount project = m_assets.sourceMounts.Single(
            static mount => mount.id == AssetSourceId.project);
        try
        {
            m_assets.ReplaceSourceMounts(
                [project, .. PluginSourceService.GetActivatableMounts(initialScan)]);
            m_catalog.Activate(initialScan);
            PublishContributors(initialScan);
            m_settings.RebuildCurrent(allowUnresolvedContributions: true);
            return initialScan;
        }
        catch (Exception candidateFailure)
        {
            var rollbackFailures = new List<Exception>();
            TryInitialRollback(
                () => m_assets.ReplaceSourceMounts([project]),
                "asset mount rollback",
                rollbackFailures);
            var empty = new PluginScanResult([], []);
            TryInitialRollback(
                () => m_catalog.Activate(empty),
                "Plugin catalog rollback",
                rollbackFailures);
            TryInitialRollback(
                () =>
                {
                    PublishContributors(empty);
                    m_settings.RebuildCurrent(allowUnresolvedContributions: true);
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
                        m_pluginRoot,
                        $"Initial Plugin candidate activation failed and was isolated: {candidateFailure.Message}")
                ]);
            m_catalog.PublishDiscovery(discovery);
            return empty;
        }
    }

    private void TryInitialRollback(
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

    private PluginScanResult CreateActiveScanSnapshot()
        => new(m_catalog.activePlugins.ToArray(), []);

    private bool RequiresCodeReload(PluginScanResult previous, PluginScanResult candidate)
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

    private string ComputeActiveFingerprint(PluginScanResult scan)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (PluginCandidate candidate in PluginSourceService.GetActivatableCandidates(scan))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(candidate.manifest.pluginId));
            hash.AppendData(Encoding.UTF8.GetBytes(candidate.contentHash));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeInstallationFingerprint(string pluginRoot)
    {
        Directory.CreateDirectory(pluginRoot);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string[] paths = Directory.EnumerateFileSystemEntries(
                pluginRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .ToArray();
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
