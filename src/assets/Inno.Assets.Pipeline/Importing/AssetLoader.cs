using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Storage;

using IOFile = System.IO.File;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Coordinates importing, persistent cataloging, canonical loading, reloading and collection
/// for one source and artifact root pair.
/// </summary>
public sealed class AssetLoader : IDisposable, IAssetReferenceResolver
{
    internal const string C_META_POSTFIX = ".imeta";

    [ThreadStatic]
    private static AssetLoader? t_activeLoader;

    private readonly SemaphoreSlim m_operationGate = new(1, 1);
    private readonly object m_asyncSync = new();
    private readonly Dictionary<string, Task<AssetObject?>> m_inFlightPathLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Task<AssetObject?>> m_inFlightIdLoads = [];
    private readonly AssetImporterRegistry m_importers;
    private readonly AssetBuildProcessorRegistry m_buildProcessors;
    private readonly Dictionary<string, AssetRecord> m_recordsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, AssetRecord> m_recordsById = [];
    private readonly Dictionary<Guid, WeakReference<AssetObject>> m_missingAssets = [];
    private readonly DependencyGraph<Guid> m_runtimeGraph = new();
    private readonly DependencyGraph<string> m_importGraph = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<AssetObject, AssetDependencySet> m_dependencyRetention = new();
    private readonly HashSet<string> m_activeImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> m_pendingImportIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly AssetArtifactStore m_artifacts;
    private readonly AssetCatalogStore m_catalog;
    private readonly AssetDiagnosticPublisher m_diagnostics;
    private readonly DiagnosticHub m_diagnosticHub;
    private readonly IdentityAllocator m_identities;
    private readonly LogRouter m_logs;
    private readonly Logger m_log;
    private readonly SerializationRegistry m_serialization;
    private readonly SerializationContext m_serializationContext;
    private readonly AssetSourcePolicy m_sourcePolicy;
    private readonly TypeCatalog m_types;
    private readonly IReadOnlyDictionary<AssetSourceId, AssetSourceMount> m_mounts;
    private readonly bool m_runtimeArtifactsOnly;

    private bool m_disposed;
    private bool m_disposeRequested;
    private long m_importerRegistryVersion = -1;
    private long m_buildProcessorRegistryVersion = -1;

    /// <summary>
    /// Creates an asset loader for one source and Library root pair.
    /// </summary>
    /// <param name="types">
    /// The immutable type-generation owner used to discover importers and resolve asset types.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used for catalog, metadata, and asset state persistence.
    /// </param>
    /// <param name="identities">
    /// The identity allocator that owns every canonical asset loaded by this instance.
    /// </param>
    /// <param name="logs">
    /// The explicitly owned router receiving importer and catalog diagnostics.
    /// </param>
    /// <param name="diagnostics">
    /// The diagnostic hub that owns import, build, catalog, and reference reports.
    /// </param>
    /// <param name="assetRoot">
    /// The absolute source root.
    /// </param>
    /// <param name="libraryRoot">
    /// The absolute rebuildable Library root.
    /// </param>
    /// <param name="sourcePolicy">
    /// The source filtering policy, or <see langword="null"/> for defaults.
    /// </param>
    public AssetLoader(
        TypeCatalog types,
        SerializationRegistry serialization,
        IdentityAllocator identities,
        DiagnosticHub diagnostics,
        LogRouter logs,
        string assetRoot,
        string libraryRoot,
        AssetSourcePolicy? sourcePolicy = null)
        : this(
            types,
            serialization,
            identities,
            diagnostics,
            logs,
            [new AssetSourceMount(AssetSourceId.project, assetRoot, isReadOnly: false)],
            libraryRoot,
            sourcePolicy)
    {
    }

    /// <summary>
    /// Creates an asset loader over one project source and zero or more isolated sources.
    /// </summary>
    /// <param name="types">
    /// The immutable type-generation owner used to discover importers and resolve asset types.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used for catalog, metadata, and asset state persistence.
    /// </param>
    /// <param name="identities">
    /// The identity allocator that owns every canonical asset loaded by this instance.
    /// </param>
    /// <param name="logs">
    /// The explicitly owned router receiving importer and catalog diagnostics.
    /// </param>
    /// <param name="diagnostics">
    /// The diagnostic hub that owns import, build, catalog, and reference reports.
    /// </param>
    /// <param name="mounts">
    /// Complete isolated source mount snapshot.
    /// </param>
    /// <param name="libraryRoot">
    /// Absolute rebuildable Library root.
    /// </param>
    /// <param name="sourcePolicy">
    /// Source filtering policy, or <see langword="null"/> for defaults.
    /// </param>
    /// <param name="runtimeArtifactsOnly">
    /// Whether to trust a deployed read-only catalog and skip every source reconciliation operation.
    /// </param>
    public AssetLoader(
        TypeCatalog types,
        SerializationRegistry serialization,
        IdentityAllocator identities,
        DiagnosticHub diagnostics,
        LogRouter logs,
        IReadOnlyList<AssetSourceMount> mounts,
        string libraryRoot,
        AssetSourcePolicy? sourcePolicy = null,
        bool runtimeArtifactsOnly = false)
        : this(
            types,
            serialization,
            identities,
            diagnostics,
            logs,
            mounts,
            libraryRoot,
            libraryRoot,
            sourcePolicy,
            runtimeArtifactsOnly)
    {
    }

    internal AssetLoader(
        TypeCatalog types,
        SerializationRegistry serialization,
        IdentityAllocator identities,
        DiagnosticHub diagnostics,
        LogRouter logs,
        IReadOnlyList<AssetSourceMount> mounts,
        string libraryRoot,
        string catalogLibraryRoot,
        AssetSourcePolicy? sourcePolicy,
        bool runtimeArtifactsOnly = false)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(mounts);
        if (mounts.Count == 0)
            throw new ArgumentException("At least one asset source mount is required.", nameof(mounts));
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root is required.", nameof(libraryRoot));
        if (string.IsNullOrWhiteSpace(catalogLibraryRoot))
            throw new ArgumentException("Catalog Library root is required.", nameof(catalogLibraryRoot));
        Dictionary<AssetSourceId, AssetSourceMount> byId = mounts.ToDictionary(static mount => mount.id);
        if (byId.Count != mounts.Count)
            throw new ArgumentException("Asset source mount IDs must be unique.", nameof(mounts));
        if (!byId.TryGetValue(AssetSourceId.project, out AssetSourceMount? project)
            || project.isReadOnly && !runtimeArtifactsOnly)
        {
            throw new ArgumentException(
                runtimeArtifactsOnly
                    ? "A project asset source mount is required."
                    : "A writable project asset source mount is required.",
                nameof(mounts));
        }

        m_types = types;
        m_serialization = serialization;
        m_serializationContext = SerializationContext.empty.With<IAssetReferenceResolver>(this);
        m_identities = identities;
        m_diagnosticHub = diagnostics;
        m_diagnostics = new AssetDiagnosticPublisher(diagnostics);
        m_logs = logs;
        m_log = logs.CreateLogger<AssetLoader>();
        m_importers = new AssetImporterRegistry(types);
        m_buildProcessors = new AssetBuildProcessorRegistry(types);
        m_mounts = byId;
        m_runtimeArtifactsOnly = runtimeArtifactsOnly;
        assetRoot = project.rootPath;
        this.libraryRoot = Path.GetFullPath(libraryRoot);
        foreach (AssetSourceMount mount in mounts)
        {
            if (mount.isReadOnly && !Directory.Exists(mount.rootPath))
            {
                throw new DirectoryNotFoundException(
                    $"Read-only asset source root '{mount.rootPath}' does not exist.");
            }

            Directory.CreateDirectory(mount.rootPath);
        }
        Directory.CreateDirectory(this.libraryRoot);
        m_sourcePolicy = sourcePolicy ?? AssetSourcePolicy.defaultPolicy;
        m_artifacts = new AssetArtifactStore(this.libraryRoot, serialization);
        m_catalog = new AssetCatalogStore(catalogLibraryRoot, serialization);
    }

    /// <summary>
    /// Gets the absolute source root.
    /// </summary>
    public string assetRoot { get; }

    /// <summary>
    /// Gets the absolute rebuildable Library root.
    /// </summary>
    public string libraryRoot { get; }

    /// <summary>
    /// Gets the derived content-addressed artifact root.
    /// </summary>
    public string artifactRoot => m_artifacts.root;

    /// <summary>
    /// Writes a source-free runtime catalog and its exact immutable artifact closure.
    /// </summary>
    /// <param name="destinationLibraryRoot">
    /// Empty destination that becomes the deployed content root.
    /// </param>
    /// <returns>
    /// Counts and source identities for the exported runtime snapshot.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a runtime-scoped asset has no complete artifact or depends on an authoring-only asset.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the destination is not empty or content cannot be copied.
    /// </exception>
    public AssetRuntimeContentInfo ExportRuntimeArtifacts(string destinationLibraryRoot)
        => ExportRuntimeArtifacts(destinationLibraryRoot, CancellationToken.None);

    /// <summary>
    /// Exports the validated runtime artifact closure while observing cooperative cancellation between files.
    /// </summary>
    /// <param name="destinationLibraryRoot">
    /// The empty directory that receives the runtime-only asset database.
    /// </param>
    /// <param name="cancellationToken">
    /// The token checked before every artifact file is copied.
    /// </param>
    /// <returns>
    /// Counts and source identities represented by the exported snapshot.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested before the snapshot finishes.
    /// </exception>
    public AssetRuntimeContentInfo ExportRuntimeArtifacts(
        string destinationLibraryRoot,
        CancellationToken cancellationToken)
        => Execute(() => ExportRuntimeArtifactsLocked(destinationLibraryRoot, cancellationToken));

    internal AssetRuntimeContentInfo ExportRuntimeArtifacts(
        string destinationLibraryRoot,
        SerializationGeneration serialization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        return Execute(() => ExportRuntimeArtifactsLocked(
            destinationLibraryRoot,
            cancellationToken,
            serialization));
    }

    /// <summary>
    /// Occurs after a loaded canonical asset is updated in place.
    /// </summary>
    public event Action<AssetObject>? AssetReloaded;

    /// <summary>
    /// Creates an isolated source-mount and catalog candidate without changing the active catalog.
    /// </summary>
    /// <param name="mounts">
    /// The complete source-mount snapshot to validate.
    /// </param>
    /// <param name="sourcePolicy">
    /// The source policy for the candidate, or <see langword="null"/> to reuse this loader's policy.
    /// </param>
    /// <returns>
    /// A candidate that owns its catalog staging storage and exposes the isolated loader to its owner.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the mount snapshot is empty, duplicated, or has no writable project source.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the active catalog cannot be copied into candidate storage.
    /// </exception>
    public AssetCatalogCandidate PrepareCatalogCandidate(
        IReadOnlyList<AssetSourceMount> mounts,
        AssetSourcePolicy? sourcePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        return Execute(() =>
        {
            string candidateLibraryRoot = Path.Combine(
                libraryRoot,
                "AssetDatabase",
                "Candidates",
                Guid.NewGuid().ToString("N"));
            AssetLoader? candidateLoader = null;
            try
            {
                m_catalog.CopyLatestTo(candidateLibraryRoot);
                candidateLoader = new AssetLoader(
                    m_types,
                    m_serialization,
                    m_identities,
                    m_diagnosticHub,
                    m_logs,
                    mounts,
                    libraryRoot,
                    candidateLibraryRoot,
                    sourcePolicy ?? m_sourcePolicy);
                return new AssetCatalogCandidate(libraryRoot, candidateLibraryRoot, candidateLoader);
            }
            catch
            {
                candidateLoader?.Dispose();
                if (Directory.Exists(candidateLibraryRoot))
                    Directory.Delete(candidateLibraryRoot, recursive: true);
                throw;
            }
        });
    }

    internal void PromoteCatalogTo(string destinationLibraryRoot)
        => Execute(() => m_catalog.PromoteTo(destinationLibraryRoot));

    /// <summary>
    /// Imports one isolated source file into metadata and a runtime artifact.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer handled the source.
    /// </returns>
    public bool Import(AssetPath path)
        => Execute(() => ImportLocked(NormalizeAssetPath(path)));

    /// <summary>
    /// Reconciles source files, metadata, artifacts and the in-memory catalog.
    /// </summary>
    public void Rescan()
        => Execute(RescanLocked);

    /// <summary>
    /// Loads a canonical asset by isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <returns>
    /// The canonical asset, or <see langword="null"/> when unavailable or incompatible.
    /// </returns>
    public AssetObject? Load(AssetPath path, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        return Execute(() => LoadPathLocked(NormalizeAssetPath(path), requestedAssetType));
    }

    /// <summary>
    /// Tries to load a canonical asset by isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <param name="asset">
    /// The canonical asset when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible asset was loaded.
    /// </returns>
    public bool TryLoad(AssetPath path, Type requestedAssetType, out AssetObject? asset)
    {
        asset = Load(path, requestedAssetType);
        return asset is not null;
    }

    /// <summary>
    /// Loads a canonical asset by persistent identity.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <returns>
    /// The canonical asset, or <see langword="null"/> when unavailable or incompatible.
    /// </returns>
    public AssetObject? Load(Guid persistentId, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        return Execute(() => LoadIdLocked(persistentId, requestedAssetType));
    }

    /// <summary>
    /// Tries to load a canonical asset by persistent identity.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <param name="asset">
    /// The canonical asset when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible asset was loaded.
    /// </returns>
    public bool TryLoad(Guid persistentId, Type requestedAssetType, out AssetObject? asset)
    {
        asset = Load(persistentId, requestedAssetType);
        return asset is not null;
    }

    /// <summary>
    /// Asynchronously loads a canonical asset by isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the current caller's wait.
    /// </param>
    /// <returns>
    /// The canonical asset, or <see langword="null"/> when unavailable or incompatible.
    /// </returns>
    public ValueTask<AssetObject?> LoadAsync(
        AssetPath path,
        Type requestedAssetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        string normalized = NormalizeAssetPath(path);
        Task<AssetObject?> operation;
        lock (m_asyncSync)
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            if (!m_inFlightPathLoads.TryGetValue(normalized, out operation!))
            {
                operation = Task.Run(() => Load(AssetPath.Parse(normalized), typeof(AssetObject)));
                m_inFlightPathLoads.Add(normalized, operation);
                _ = operation.ContinueWith(
                    _ => RemovePathOperation(normalized, operation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        return AwaitSharedLoad(operation, requestedAssetType, cancellationToken);
    }

    /// <summary>
    /// Asynchronously loads a canonical asset by persistent identity.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="requestedAssetType">
    /// The required assignable asset type.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the current caller's wait.
    /// </param>
    /// <returns>
    /// The canonical asset, or <see langword="null"/> when unavailable or incompatible.
    /// </returns>
    public ValueTask<AssetObject?> LoadAsync(
        Guid persistentId,
        Type requestedAssetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        Task<AssetObject?> operation;
        lock (m_asyncSync)
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            if (!m_inFlightIdLoads.TryGetValue(persistentId, out operation!))
            {
                operation = Task.Run(() => Load(persistentId, typeof(AssetObject)));
                m_inFlightIdLoads.Add(persistentId, operation);
                _ = operation.ContinueWith(
                    _ => RemoveIdOperation(persistentId, operation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        return AwaitSharedLoad(operation, requestedAssetType, cancellationToken);
    }

    /// <summary>
    /// Resolves a serialized reference or creates a persistent missing placeholder.
    /// </summary>
    /// <param name="persistentId">
    /// The referenced persistent identity.
    /// </param>
    /// <param name="stableTypeId">
    /// The referenced stable asset type identity.
    /// </param>
    /// <param name="lastKnownPath">
    /// The last known source-relative path.
    /// </param>
    /// <param name="expectedType">
    /// The declared destination type.
    /// </param>
    /// <returns>
    /// A compatible canonical asset or missing placeholder.
    /// </returns>
    public AssetObject ResolveReference(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException("A serialized asset reference has an empty persistent identity.");
        return Execute(() => ResolveReferenceLocked(persistentId, stableTypeId, lastKnownPath, expectedType));
    }

    AssetObject IAssetReferenceResolver.Resolve(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
    {
        try
        {
            return ResolveReference(persistentId, stableTypeId, lastKnownPath, expectedType);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Asset reference '{persistentId:D}' at '{propertyPath}' cannot be resolved as " +
                $"'{expectedType.FullName}'.",
                exception);
        }
    }

    /// <summary>
    /// Saves an asset back to its current source path.
    /// </summary>
    /// <param name="asset">
    /// The asset to export.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer exported the asset.
    /// </returns>
    public bool Save(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.assetPath.ToString()))
            throw new InvalidOperationException("An unsaved asset requires an explicit source-relative path.");
        return Save(asset.assetPath, asset);
    }

    /// <summary>
    /// Saves an asset to its initial or existing isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="asset">
    /// The asset to export.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer exported the asset.
    /// </returns>
    public bool Save(AssetPath path, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string normalized = NormalizeAssetPath(path);
        return Execute(() => SaveLocked(normalized, asset));
    }

    /// <summary>
    /// Applies normalized source file changes to the persistent catalog.
    /// </summary>
    /// <param name="changes">
    /// The normalized source changes.
    /// </param>
    public void ApplySourceChanges(IReadOnlyList<AssetChangedEvent> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Execute(() => ApplySourceChangesLocked(changes));
    }

    /// <summary>
    /// Waits for pending import and build work.
    /// </summary>
    public void WaitForIdle()
        => Execute(static () => { });

    /// <summary>
    /// Collects unreachable content-addressed artifacts.
    /// </summary>
    /// <param name="gracePeriod">
    /// The minimum age of an unreachable bundle.
    /// </param>
    /// <param name="maximumSizeBytes">
    /// The cache size limit, or zero for no limit.
    /// </param>
    /// <returns>
    /// The number of removed artifact bundles.
    /// </returns>
    public int CollectArtifacts(TimeSpan gracePeriod, long maximumSizeBytes)
    {
        return Execute(() =>
        {
            HashSet<string> reachable = [];
            foreach (AssetRecord record in m_recordsByPath.Values)
            {
                AddArtifactKey(reachable, record.meta.artifactKey);
                AddArtifactKey(reachable, record.meta.lastSuccessfulArtifactKey);
            }
            return m_artifacts.Collect(reachable, gracePeriod, maximumSizeBytes);
        });
    }

    /// <summary>
    /// Refreshes extension registries and reimports affected sources when their snapshot changed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the importer registry changed.
    /// </returns>
    public bool RefreshRegistries()
    {
        return Execute(() =>
        {
            long version = m_importers.snapshotVersion;
            long buildVersion = m_buildProcessors.snapshotVersion;
            if (version == m_importerRegistryVersion &&
                buildVersion == m_buildProcessorRegistryVersion)
                return false;
            RescanLocked();
            return true;
        });
    }

    /// <summary>
    /// Captures the current failure identity of every writable source without exposing catalog internals.
    /// </summary>
    /// <returns>
    /// An immutable health snapshot suitable for validating a later candidate generation.
    /// </returns>
    public AssetImportHealthSnapshot CaptureWritableImportHealth()
        => new(Execute(() => m_recordsByPath.Values
            .Where(record =>
                IsMounted(record.relativePath)
                && !GetMount(record.relativePath).isReadOnly
                && record.meta.importStatus is (int)AssetImportStatus.Failed
                    or (int)AssetImportStatus.Conflict)
            .Select(static record => new AssetImportFailureFingerprint(
                record.relativePath,
                record.meta.importStatus,
                record.meta.sourceHash,
                record.meta.importerId,
                string.Join("\n", record.meta.diagnostics)))
            .ToHashSet()));

    /// <summary>
    /// Finds writable-source failures that are new or changed relative to an earlier health snapshot.
    /// </summary>
    /// <param name="baseline">
    /// The health snapshot captured before the candidate generation was activated.
    /// </param>
    /// <returns>
    /// A deterministic path-ordered collection containing only introduced or changed failures.
    /// </returns>
    public IReadOnlyList<AssetImportFailure> FindIntroducedImportFailures(
        AssetImportHealthSnapshot baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return Execute(() => m_recordsByPath.Values
            .Where(record =>
                IsMounted(record.relativePath)
                && !GetMount(record.relativePath).isReadOnly
                && record.meta.importStatus is (int)AssetImportStatus.Failed
                    or (int)AssetImportStatus.Conflict)
            .Select(static record => new AssetImportFailureFingerprint(
                record.relativePath,
                record.meta.importStatus,
                record.meta.sourceHash,
                record.meta.importerId,
                string.Join("\n", record.meta.diagnostics)))
            .Where(failure => !baseline.failures.Contains(failure))
            .OrderBy(static failure => failure.relativePath, StringComparer.Ordinal)
            .Select(static failure => new AssetImportFailure(
                failure.relativePath,
                failure.diagnostics))
            .ToArray());
    }

    /// <summary>
    /// Tries to get a catalog snapshot by isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="info">
    /// The immutable catalog snapshot when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source is cataloged.
    /// </returns>
    public bool TryGetInfo(AssetPath path, out AssetInfo? info)
    {
        if (!TryNormalizeCatalogPath(path, out string normalized))
        {
            info = null;
            return false;
        }
        AssetInfo? result = Execute(() => CreateInfo(FindRecordLocked(normalized)));
        info = result;
        return result is not null;
    }

    /// <summary>
    /// Tries to get a catalog snapshot by persistent identity.
    /// </summary>
    /// <param name="persistentId">
    /// The stable persistent identity used for lookup.
    /// </param>
    /// <param name="info">
    /// The resolved immutable metadata returned to the caller.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetInfo(Guid persistentId, out AssetInfo? info)
    {
        AssetInfo? result = Execute(() => m_recordsById.TryGetValue(persistentId, out AssetRecord? record)
            ? CreateInfo(record)
            : null);
        info = result;
        return result is not null;
    }

    /// <summary>
    /// Tries to resolve a named output from the current artifact bundle.
    /// </summary>
    /// <param name="persistentId">
    /// The stable persistent identity used for lookup.
    /// </param>
    /// <param name="outputName">
    /// The stable artifact output name used for lookup.
    /// </param>
    /// <param name="artifact">
    /// The resolved immutable artifact payload returned to the caller.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
    {
        AssetArtifactInfo? result = Execute(() =>
        {
            if (!m_recordsById.TryGetValue(persistentId, out AssetRecord? record))
                return null;
            return m_artifacts.TryGet(
                new AssetArtifactKey(record.meta.artifactKey),
                outputName,
                out AssetArtifactInfo? found)
                ? found
                : null;
        });
        artifact = result;
        return result is not null;
    }

    /// <summary>
    /// Builds and validates the requested artifact asynchronously before publishing it.
    /// </summary>
    /// <param name="definition">
    /// The build definition asset.
    /// </param>
    /// <param name="inputs">
    /// The immutable input catalog snapshots.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the candidate build.
    /// </param>
    /// <returns>
    /// The content-addressed output bundle key.
    /// </returns>
    public ValueTask<AssetArtifactKey> BuildAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inputs);
        Guid targetId = definition.identity.persistentId;
        string displayName = string.IsNullOrWhiteSpace(definition.assetPath.ToString())
            ? definition.GetType().Name
            : definition.assetPath.ToString();
        AssetArtifactKey key = Execute(() =>
        {
            try
            {
                AssetBuildProcessor processor = m_buildProcessors.Find(definition.GetType())
                    ?? throw new InvalidOperationException(
                        $"No asset build processor accepts '{definition.GetType().FullName}'.");
                var output = new AssetArtifactWriter();
                processor.BuildInternalAsync(definition, inputs, output, cancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (output.outputs.Count == 0)
                    throw new InvalidOperationException("An asset build processor produced no outputs.");
                string fingerprint = CreateBuildFingerprint(processor, definition, inputs);
                AssetArtifactKey result = m_artifacts.Commit(fingerprint, output.outputs);
                m_diagnostics.PublishBuild(targetId, displayName, output.diagnostics);
                return result;
            }
            catch (Exception exception)
            {
                m_diagnostics.PublishBuildFailure(targetId, displayName, exception);
                m_log.Write(
                    LogLevel.Error,
                    "Asset build for '{0}' failed: {1}",
                    [displayName, exception]);
                throw;
            }
        });
        return ValueTask.FromResult(key);
    }

    /// <summary>
    /// Tries to resolve a persistent identity without loading the asset.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="persistentId">
    /// The resolved identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when catalog metadata exists.
    /// </returns>
    public bool TryGetPersistentId(AssetPath path, out Guid persistentId)
    {
        if (!TryNormalizeCatalogPath(path, out string normalized))
        {
            persistentId = Guid.Empty;
            return false;
        }
        Guid result = Execute(() => FindRecordLocked(normalized)?.persistentId ?? Guid.Empty);
        persistentId = result;
        return result != Guid.Empty;
    }

    /// <summary>
    /// Tries to resolve the concrete asset type without loading it.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="assetType">
    /// The resolved type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type can be resolved.
    /// </returns>
    public bool TryGetAssetType(AssetPath path, out Type? assetType)
    {
        if (!TryNormalizeCatalogPath(path, out string normalized))
        {
            assetType = null;
            return false;
        }
        Type? result = Execute(() => ResolveRecordType(FindRecordLocked(normalized)));
        assetType = result;
        return result is not null;
    }

    /// <summary>
    /// Gets isolated source paths of all canonical loaded assets.
    /// </summary>
    /// <returns>
    /// A stable isolated path snapshot.
    /// </returns>
    public IReadOnlyList<AssetPath> GetLoadedPaths()
        => Execute(() => m_recordsByPath.Values
            .Where(static record => record.asset is not null)
            .Select(static record => AssetPath.Parse(record.relativePath))
            .OrderBy(static path => path.source.value, StringComparer.Ordinal)
            .ThenBy(static path => path.localPath, StringComparer.Ordinal)
            .ToArray());

    /// <summary>
    /// Gets direct or transitive runtime dependencies of an asset.
    /// </summary>
    /// <param name="asset">
    /// The asset to query.
    /// </param>
    /// <param name="recursive">
    /// Whether transitive dependencies should be included.
    /// </param>
    /// <returns>
    /// The persistent dependency descriptors.
    /// </returns>
    public IReadOnlyList<AssetDependency> GetDependencies(AssetObject asset, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Execute(() => GetDependenciesLocked(asset.identity.persistentId, recursive));
    }

    /// <summary>
    /// Gets source import dependencies that invalidate an asset artifact.
    /// </summary>
    /// <param name="asset">
    /// The asset to query.
    /// </param>
    /// <param name="recursive">
    /// Whether transitive source dependencies should be included.
    /// </param>
    /// <returns>
    /// Canonical isolated source paths in stable order.
    /// </returns>
    public IReadOnlyList<AssetPath> GetImportDependencies(AssetObject asset, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Execute(() =>
        {
            string path = NormalizeRelativePath(asset.assetPath.ToString());
            return m_importGraph.GetDependencies(path, recursive)
                .Select(AssetPath.Parse)
                .OrderBy(static value => value.source.value, StringComparer.Ordinal)
                .ThenBy(static value => value.localPath, StringComparer.Ordinal)
                .ToArray();
        });
    }

    /// <summary>
    /// Gets an engine-known reference diagnostic snapshot.
    /// </summary>
    /// <param name="asset">
    /// The asset to inspect.
    /// </param>
    /// <returns>
    /// The reference diagnostic snapshot.
    /// </returns>
    public AssetReferenceInfo GetReferenceInfo(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Execute(() => GetReferenceInfoLocked(asset));
    }

    /// <summary>
    /// Collects canonical assets that have no external managed references.
    /// </summary>
    /// <returns>
    /// The number of released canonical assets.
    /// </returns>
    public int UnloadUnusedAssets()
        => Execute(UnloadUnusedAssetsLocked);

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        Task[] pending;
        lock (m_asyncSync)
        {
            if (m_disposeRequested)
                return;
            m_disposeRequested = true;
            pending = m_inFlightPathLoads.Values
                .Concat(m_inFlightIdLoads.Values)
                .Distinct()
                .Cast<Task>()
                .ToArray();
        }
        if (pending.Length > 0)
        {
            try
            {
                Task.WhenAll(pending).GetAwaiter().GetResult();
            }
            catch
            {
                // Load failures are observed by their callers and do not prevent deterministic disposal.
            }
        }
        Execute(DisposeLocked, allowDisposed: true);
        m_operationGate.Dispose();
    }

    private bool ImportLocked(string relativePath)
    {
        if (m_activeImports.Contains(relativePath))
            return true;
        string sourcePath = GetSourcePath(relativePath);
        if (!IOFile.Exists(sourcePath))
            return false;
        AssetImporter? importer = m_importers.FindByPath(relativePath);
        if (importer is null)
            return false;

        AssetRecord? existingRecord = FindRecordLocked(relativePath);
        AssetSourceMount sourceMount = GetMount(relativePath);
        if (sourceMount.isReadOnly && !IOFile.Exists(GetMetaPath(relativePath)))
        {
            throw new InvalidDataException(
                $"Read-only source '{relativePath}' requires a valid '{C_META_POSTFIX}' sidecar.");
        }
        Guid persistentId = existingRecord?.persistentId
            ?? m_pendingImportIds.GetValueOrDefault(relativePath);
        if (persistentId == Guid.Empty &&
            TryReadSourceMeta(GetMetaPath(relativePath), out AssetSourceMeta sourceMeta))
        {
            persistentId = sourceMeta.persistentId;
            AssetRecord? sameId = FindRecordByIdWithoutLoading(persistentId);
            if (sameId is not null &&
                !string.Equals(sameId.relativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                if (sourceMount.isReadOnly)
                {
                    throw new InvalidDataException(
                        $"Read-only source '{relativePath}' duplicates persistent ID '{persistentId}'.");
                }
                persistentId = Guid.NewGuid();
            }
        }
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();
        m_pendingImportIds[relativePath] = persistentId;
        m_activeImports.Add(relativePath);
        try
        {
            byte[] sourceBytes = ReadStableSourceBytes(sourcePath, out AssetSourceFileStamp sourceStamp);
            try
            {
                ImportBuild build = BuildImportLocked(
                    relativePath,
                    sourceBytes,
                    importer,
                    persistentId,
                    sourceStamp);
                try
                {
                    CommitBuildLocked(build, writeSource: false, sourceBytes);
                }
                finally
                {
                    AssetRuntimeHost.Release(build.asset);
                }
                return true;
            }
            catch (Exception exception)
            {
                RecordImportFailureLocked(
                    relativePath,
                    sourceBytes,
                    importer,
                    persistentId,
                    exception);
                return false;
            }
        }
        catch (IOException exception)
        {
            RecordImportFailureLocked(relativePath, [], importer, persistentId, exception);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordImportFailureLocked(relativePath, [], importer, persistentId, exception);
            return false;
        }
        finally
        {
            m_activeImports.Remove(relativePath);
            m_pendingImportIds.Remove(relativePath);
        }
    }

    private void RecordImportFailureLocked(
        string relativePath,
        byte[] sourceBytes,
        AssetImporter importer,
        Guid persistentId,
        Exception exception)
    {
        AssetRecord record = FindRecordLocked(relativePath) ?? new AssetRecord
        {
            relativePath = relativePath,
            persistentId = persistentId
        };
        record.relativePath = relativePath;
        record.persistentId = persistentId;
        record.meta.relativePath = relativePath;
        record.meta.persistentId = persistentId;
        record.meta.sourceHash = sourceBytes.Length == 0 ? string.Empty : ComputeSha256Hex(sourceBytes);
        record.meta.importerId = importer.importerId;
        record.meta.importerImplementationFingerprint = GetImporterImplementationFingerprint(importer);
        if (record.meta.stableAssetTypeId == Guid.Empty &&
            m_types.TryGetTypeRef(importer.targetAssetType, out TypeRef importerTypeRef))
        {
            record.meta.stableAssetTypeId = importerTypeRef.stableId;
            record.stableTypeId = importerTypeRef.stableId;
        }
        record.meta.importStatus = (int)AssetImportStatus.Failed;
        record.meta.diagnostics = [$"{exception.GetType().Name}: {exception.Message}"];
        AddOrReplaceRecordLocked(record);
        WriteSourceMeta(record.meta);
        CommitCatalogLocked();
        m_log.Write(
            LogLevel.Error,
            "Asset import for '{0}' failed: {1}",
            [relativePath, exception]);
    }

    private ImportBuild BuildImportLocked(
        string relativePath,
        byte[] sourceBytes,
        AssetImporter importer,
        Guid persistentId,
        AssetSourceFileStamp sourceStamp = default)
    {
        string sourceHash = ComputeSha256Hex(sourceBytes);
        var context = new AssetImportContext(
            relativePath,
            GetSourcePath(relativePath),
            sourceBytes,
            sourceHash,
            persistentId,
            m_types,
            m_serialization,
            this,
            (dependencyPath, dependencyType) =>
            {
                string normalizedDependency = NormalizeRelativePath(dependencyPath);
                ValidateSourceReferenceLocked(relativePath, normalizedDependency);
                return LoadPathLocked(normalizedDependency, dependencyType);
            },
            sourceDependencyPath =>
            {
                string normalizedDependency = NormalizeRelativePath(sourceDependencyPath);
                ValidateSourceReferenceLocked(relativePath, normalizedDependency);
                string physicalPath = GetSourcePath(normalizedDependency);
                if (!IOFile.Exists(physicalPath))
                {
                    throw new FileNotFoundException(
                        $"Import source dependency '{normalizedDependency}' does not exist.",
                        physicalPath);
                }
                return ReadStableSourceBytes(physicalPath, out _);
            });
        AssetImportProduct product = importer
            .ImportInternalAsync(context, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        AssetDeploymentScope deploymentScope = importer.deploymentScope;
        if (!Enum.IsDefined(deploymentScope))
        {
            throw new InvalidOperationException(
                $"Importer '{importer.GetType().FullName}' declares an unsupported deployment scope.");
        }
        bool hasRuntimeOutput = product.outputs.ContainsKey("runtime");
        if (deploymentScope == AssetDeploymentScope.Runtime && !hasRuntimeOutput)
        {
            throw new InvalidOperationException(
                $"Runtime importer '{importer.GetType().FullName}' must write a 'runtime' artifact output.");
        }
        if (deploymentScope == AssetDeploymentScope.AuthoringOnly && hasRuntimeOutput)
        {
            throw new InvalidOperationException(
                $"Authoring-only importer '{importer.GetType().FullName}' cannot write a 'runtime' artifact output.");
        }
        if (!importer.targetAssetType.IsInstanceOfType(product.asset))
        {
            throw new InvalidOperationException(
                $"Importer '{importer.GetType().FullName}' returned '{product.asset.GetType().FullName}' " +
                $"instead of '{importer.targetAssetType.FullName}'.");
        }
        if (!m_types.TryGetTypeRef(product.asset.GetType(), out TypeRef assetTypeRef))
        {
            throw new InvalidOperationException(
                $"Imported asset type '{product.asset.GetType().FullName}' requires a StableTypeId.");
        }

        AssetDependency[] runtimeDependencies = ResolveDeclaredDependenciesLocked(context);
        byte[] state = CaptureAssetState(product.asset);
        AssetRuntimeHost.Initialize(
            product.asset,
            AssetPath.Parse(relativePath),
            sourceHash,
            product.runtimePayload,
            false,
            1);
        var outputs = new Dictionary<string, ReadOnlyMemory<byte>>(product.outputs, StringComparer.Ordinal);
        if (!outputs.TryAdd("asset-state", state))
            throw new InvalidOperationException("The artifact output name 'asset-state' is reserved.");
        string implementationFingerprint = GetImporterImplementationFingerprint(importer);
        var meta = new AssetMeta
        {
            persistentId = persistentId,
            relativePath = relativePath,
            sourceHash = sourceHash,
            importerId = importer.importerId,
            deploymentScope = (int)deploymentScope,
            stableAssetTypeId = assetTypeRef.stableId,
            assetStateBytes = state,
            runtimeDependencies = runtimeDependencies.Select(ToData).ToArray(),
            importDependencies = context.importDependencies
                .Select(dependency => CreateImportDependencyDataLocked(context.assetPath.ToString(), dependency))
                .ToArray(),
            importStatus = (int)AssetImportStatus.Imported,
            importerImplementationFingerprint = implementationFingerprint,
            diagnostics = product.diagnostics.ToArray()
        };
        ApplySourceStamp(meta, sourceStamp);
        ValidateImportDependenciesLocked(meta);
        return new ImportBuild(
            meta,
            product.asset,
            product.runtimePayload.ToArray(),
            outputs,
            runtimeDependencies);
    }

    private void CommitBuildLocked(ImportBuild build, bool writeSource, byte[] sourceBytes)
    {
        AssetRecord? existing = FindRecordLocked(build.meta.relativePath);
        AssetObject? canonical = existing?.asset;
        byte[]? previousState = null;
        byte[]? previousPayload = null;
        string previousPath = string.Empty;
        string previousHash = string.Empty;
        long previousVersion = 0;
        bool previousMissing = false;
        if (canonical is not null)
        {
            if (canonical.GetType() != build.asset.GetType())
            {
                AssetObject replaced = canonical;
                AssetRuntimeHost.Initialize(
                    replaced,
                    AssetPath.Parse(build.meta.relativePath),
                    build.meta.sourceHash,
                    replaced.runtimePayload,
                    true,
                    replaced.contentVersion + 1);
                AssetRuntimeHost.Release(replaced);
                m_identities.Unregister(replaced);
                existing!.asset = null;
                canonical = null;
                PublishReloaded(replaced);
            }
        }
        if (canonical is not null)
        {
            previousState = CaptureAssetState(canonical);
            previousPayload = canonical.runtimePayload.ToArray();
            previousPath = canonical.assetPath.ToString();
            previousHash = AssetRuntimeHost.GetSourceHash(canonical);
            previousVersion = canonical.contentVersion;
            previousMissing = canonical.isMissing;
            try
            {
                RestoreAssetState(canonical, build.meta.assetStateBytes);
                AssetRuntimeHost.Initialize(
                    canonical,
                    AssetPath.Parse(build.meta.relativePath),
                    build.meta.sourceHash,
                    build.payload,
                    false,
                    previousVersion + 1);
            }
            catch
            {
                RestoreCanonical(
                    canonical,
                    previousState,
                    previousPayload,
                    previousPath,
                    previousHash,
                    previousMissing,
                    previousVersion);
                throw;
            }
        }

        string sourcePath = GetSourcePath(build.meta.relativePath);
        string metaPath = GetMetaPath(build.meta.relativePath);
        FileSnapshot sourceSnapshot = CaptureFile(sourcePath);
        FileSnapshot metaSnapshot = CaptureFile(metaPath);
        try
        {
            if (writeSource)
                WriteAtomic(sourcePath, sourceBytes);
            if (!AssetSourceFileStamp.TryCapture(sourcePath, out AssetSourceFileStamp sourceStamp))
                throw new IOException($"Source '{build.meta.relativePath}' changed while its import was committing.");
            if (!writeSource && !SourceStampMatches(build.meta, sourceStamp))
            {
                throw new IOException(
                    $"Source '{build.meta.relativePath}' changed while its importer was running.");
            }
            ApplySourceStamp(build.meta, sourceStamp);
            ValidateImportDependencySnapshotsLocked(build.meta);
            AssetArtifactKey artifactKey = m_artifacts.Commit(
                CreateImportFingerprint(build.meta),
                build.outputs);
            build.meta.artifactKey = artifactKey.value;
            build.meta.lastSuccessfulArtifactKey = artifactKey.value;
            WriteSourceMeta(build.meta);
        }
        catch
        {
            if (writeSource)
                RestoreFile(sourcePath, sourceSnapshot);
            RestoreFile(metaPath, metaSnapshot);
            if (canonical is not null && previousState is not null && previousPayload is not null)
            {
                RestoreCanonical(
                    canonical,
                    previousState,
                    previousPayload,
                    previousPath,
                    previousHash,
                    previousMissing,
                    previousVersion);
            }
            throw;
        }

        AssetRecord record = existing ?? new AssetRecord();
        record.relativePath = build.meta.relativePath;
        record.persistentId = build.meta.persistentId;
        record.stableTypeId = build.meta.stableAssetTypeId;
        record.meta = build.meta;
        record.payload = build.payload;
        record.importerGeneration = m_importers.GetGeneration(build.meta.importerId);
        if (canonical is not null)
            record.asset = canonical;
        AddOrReplaceRecordLocked(record);
        UpdateGraphsLocked(record);
        CommitCatalogLocked();
        if (canonical is not null)
        {
            AttachDependenciesLocked(record);
            PublishReloaded(canonical);
        }
    }

    private bool SaveLocked(string relativePath, AssetObject asset)
    {
        if (GetMount(relativePath).isReadOnly)
            throw new InvalidOperationException($"Asset source '{relativePath}' is read-only.");
        if (!string.IsNullOrWhiteSpace(asset.assetPath.ToString()) &&
            !string.Equals(NormalizeRelativePath(asset.assetPath.ToString()), relativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset '{asset.assetPath.ToString()}' cannot be saved to unrelated path '{relativePath}' without creating a new asset.");
        }
        AssetImporter? importer = m_importers.FindByPath(relativePath);
        if (importer is null || !importer.targetAssetType.IsInstanceOfType(asset))
            return false;
        ReadOnlyMemory<byte>? exported = importer
            .ExportInternalAsync(
                new AssetExportContext(m_types, m_serialization),
                asset,
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (exported is null)
            return false;
        byte[] sourceBytes = exported.Value.ToArray();
        Guid persistentId = asset.identity.persistentId;
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();
        ImportBuild build = BuildImportLocked(relativePath, sourceBytes, importer, persistentId);
        AssetRecord? provisionalRecord = null;
        bool registeredHere = false;
        if (string.IsNullOrWhiteSpace(asset.assetPath.ToString()))
        {
            m_identities.InitializePersistentIdentity(asset, persistentId);
            registeredHere = m_identities.Register(asset, persistentId);
            provisionalRecord = new AssetRecord
            {
                relativePath = relativePath,
                persistentId = persistentId,
                stableTypeId = build.meta.stableAssetTypeId,
                meta = build.meta,
                payload = build.payload,
                asset = asset
            };
            AddOrReplaceRecordLocked(provisionalRecord);
        }
        try
        {
            try
            {
                CommitBuildLocked(build, writeSource: true, sourceBytes);
            }
            finally
            {
                AssetRuntimeHost.Release(build.asset);
            }
        }
        catch
        {
            if (provisionalRecord is not null)
                RemoveRecordLocked(provisionalRecord, removeGeneratedFiles: false);
            if (registeredHere)
                m_identities.Unregister(asset);
            throw;
        }
        AssetRecord committed = m_recordsByPath[relativePath];
        if (committed.asset is null)
        {
            committed.asset = asset;
            if (asset.identity.runtimeId is null)
                m_identities.Register(asset, persistentId);
            AssetRuntimeHost.Initialize(
                asset,
                AssetPath.Parse(relativePath),
                build.meta.sourceHash,
                build.payload,
                false,
                1);
            AttachDependenciesLocked(committed);
        }
        return true;
    }

    private AssetObject? LoadPathLocked(string relativePath, Type requestedAssetType)
    {
        AssetRecord? record = FindRecordLocked(relativePath);
        bool sourceExists = IOFile.Exists(GetSourcePath(relativePath));
        bool stale = false;
        bool catalogChanged = false;
        if (record is not null && sourceExists)
            stale = IsStale(record, out catalogChanged);
        if (catalogChanged && !stale)
            CommitCatalogLocked();
        if (record is null || stale)
        {
            if (!ImportLocked(relativePath))
                return record is null ? null : LoadRecordLocked(record, requestedAssetType);
            record = FindRecordLocked(relativePath);
        }
        return record is null ? null : LoadRecordLocked(record, requestedAssetType);
    }

    private AssetObject? LoadIdLocked(Guid persistentId, Type requestedAssetType)
    {
        if (persistentId == Guid.Empty)
            return null;
        if (!m_recordsById.TryGetValue(persistentId, out AssetRecord? record))
        {
            LoadCatalogLocked();
            if (!m_recordsById.TryGetValue(persistentId, out record))
                return null;
        }
        if (record.meta.isTombstone)
        {
            return record.asset is null
                ? null
                : LoadRecordLocked(record, requestedAssetType);
        }

        // A source-side identity can be indexed before its body is imported. Route identity loads
        // through the same freshness gate as path loads so pending records never hydrate an empty
        // or stale artifact merely because their sidecar was discovered first. The sidecar check
        // keeps a newly created asset at the same path from satisfying an older identity lookup.
        bool ownsCurrentSource = IOFile.Exists(GetSourcePath(record.relativePath))
            && TryReadSourceMeta(GetMetaPath(record.relativePath), out AssetSourceMeta sourceMeta)
            && sourceMeta.persistentId == persistentId;
        return ownsCurrentSource
            ? LoadPathLocked(record.relativePath, requestedAssetType)
            : LoadRecordLocked(record, requestedAssetType);
    }

    private AssetObject? LoadRecordLocked(AssetRecord record, Type requestedAssetType)
    {
        Type? actualType = ResolveRecordType(record);
        if (actualType is null || !requestedAssetType.IsAssignableFrom(actualType))
            return null;
        if (record.asset is not null)
            return requestedAssetType.IsInstanceOfType(record.asset) ? record.asset : null;

        var transaction = new AssetLoadTransaction();
        try
        {
            PrepareShellsLocked(record, transaction);
            foreach (AssetRecord created in transaction.createdRecords)
                HydrateRecordLocked(created);
            foreach (AssetRecord created in transaction.createdRecords)
                AttachDependenciesLocked(created);
            return record.asset;
        }
        catch
        {
            RollbackLoadLocked(transaction);
            throw;
        }
    }

    private void PrepareShellsLocked(AssetRecord record, AssetLoadTransaction transaction)
    {
        if (record.asset is not null)
            return;
        Type type = ResolveRecordType(record)
            ?? throw new InvalidOperationException(
                $"Asset '{record.relativePath}' has unknown stable type '{record.stableTypeId}'.");
        AssetObject shell;
        try
        {
            shell = (AssetObject)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException("Activator returned null."));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Asset type '{type.FullName}' requires a parameterless constructor.", exception);
        }
        m_identities.InitializePersistentIdentity(shell, record.persistentId);
        m_identities.Register(shell, record.persistentId);
        record.asset = shell;
        transaction.createdRecords.Add(record);

        foreach (AssetDependency dependency in GetDirectDependencies(record.meta))
        {
            AssetRecord? dependencyRecord = FindDependencyRecordLocked(dependency);
            if (dependencyRecord is not null)
                PrepareShellsLocked(dependencyRecord, transaction);
        }
    }

    private void HydrateRecordLocked(AssetRecord record)
    {
        AssetObject asset = record.asset!;
        RestoreAssetState(asset, record.meta.assetStateBytes);
        byte[] payload = m_artifacts.Read(
            new AssetArtifactKey(record.meta.artifactKey),
            "runtime");
        if (payload.Length == 0 && record.payload.Length > 0)
            payload = record.payload;
        record.payload = payload;
        bool isMissing = record.meta.importStatus == (int)AssetImportStatus.Missing;
        AssetRuntimeHost.Initialize(
            asset,
            AssetPath.Parse(record.relativePath),
            record.meta.sourceHash,
            payload,
            isMissing,
            1);
    }

    private void AttachDependenciesLocked(AssetRecord record)
    {
        if (record.asset is null)
            return;
        var dependencies = new List<AssetObject>();
        foreach (AssetDependency dependency in GetDirectDependencies(record.meta))
        {
            AssetRecord? dependencyRecord = FindDependencyRecordLocked(dependency);
            AssetObject dependencyAsset = dependencyRecord?.asset
                ?? ResolveReferenceLocked(
                    dependency.persistentId,
                    dependency.type.stableId,
                    dependency.lastKnownPath,
                    ResolveDependencyExpectedType(dependency));
            dependencies.Add(dependencyAsset);
        }
        m_dependencyRetention.Remove(record.asset);
        m_dependencyRetention.Add(record.asset, new AssetDependencySet(dependencies.ToArray()));
    }

    private AssetObject ResolveReferenceLocked(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType)
    {
        AssetObject? loaded = LoadIdLocked(persistentId, expectedType);
        if (loaded is null && !string.IsNullOrWhiteSpace(lastKnownPath))
            loaded = LoadPathLocked(NormalizeRelativePath(lastKnownPath), expectedType);
        if (loaded is not null)
            return loaded;
        m_diagnostics.PublishMissingReference(persistentId, lastKnownPath, expectedType);
        if (m_missingAssets.TryGetValue(persistentId, out WeakReference<AssetObject>? weak) &&
            weak.TryGetTarget(out AssetObject? existing) && expectedType.IsInstanceOfType(existing))
        {
            return existing;
        }
        Type type = ResolveDependencyExpectedType(
            new AssetDependency(persistentId, new TypeRef(stableTypeId), lastKnownPath));
        if (type == typeof(MissingAsset) &&
            !expectedType.IsAbstract &&
            !expectedType.IsInterface &&
            typeof(AssetObject).IsAssignableFrom(expectedType))
        {
            type = expectedType;
        }
        if (!expectedType.IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Missing asset type '{type.FullName}' cannot be assigned to '{expectedType.FullName}'.");
        }
        AssetObject missing = (AssetObject)(Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException($"Missing asset type '{type.FullName}' cannot be created."));
        m_identities.InitializePersistentIdentity(missing, persistentId);
        AssetRuntimeHost.Initialize(
            missing,
            AssetPath.Parse(lastKnownPath),
            string.Empty,
            ReadOnlyMemory<byte>.Empty,
            true,
            0);
        m_missingAssets[persistentId] = new WeakReference<AssetObject>(missing);
        return missing;
    }

    private AssetDependency[] ResolveDeclaredDependenciesLocked(AssetImportContext context)
    {
        var result = context.runtimeDependencies.ToDictionary(static value => value.persistentId);
        foreach (Guid persistentId in result.Keys.ToArray())
        {
            AssetDependency dependency = result[persistentId];
            string dependencyPath = dependency.lastKnownPath;
            if (m_recordsById.TryGetValue(dependency.persistentId, out AssetRecord? record))
            {
                dependencyPath = record.relativePath;
                result[persistentId] = new AssetDependency(
                    dependency.persistentId,
                    dependency.type,
                    dependencyPath);
            }
            if (!string.IsNullOrWhiteSpace(dependencyPath))
                ValidateSourceReferenceLocked(context.assetPath.ToString(), NormalizeRelativePath(dependencyPath));
        }
        foreach (string path in context.runtimeDependencyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = NormalizeRelativePath(path);
            ValidateSourceReferenceLocked(context.assetPath.ToString(), normalized);
            if (m_activeImports.Contains(normalized))
            {
                Guid pendingId = m_pendingImportIds[normalized];
                AssetImporter? pendingImporter = m_importers.FindByPath(normalized);
                TypeRef typeRef = pendingImporter is not null &&
                    m_types.TryGetTypeRef(pendingImporter.targetAssetType, out TypeRef pendingTypeRef)
                    ? pendingTypeRef
                    : default;
                result[pendingId] = new AssetDependency(pendingId, typeRef, normalized);
                continue;
            }
            AssetRecord? dependencyRecord = FindRecordLocked(normalized);
            bool dependencyStale = dependencyRecord is null ||
                                   IsStale(dependencyRecord, out _);
            if (dependencyStale && !ImportLocked(normalized))
            {
                throw new InvalidOperationException(
                    $"Runtime dependency '{normalized}' referenced by '{context.assetPath}' cannot be imported.");
            }
            dependencyRecord = FindRecordLocked(normalized)
                ?? throw new InvalidOperationException($"Runtime dependency '{normalized}' has no metadata.");
            result[dependencyRecord.persistentId] = new AssetDependency(
                dependencyRecord.persistentId,
                new TypeRef(dependencyRecord.stableTypeId),
                dependencyRecord.relativePath);
        }
        return result.Values.OrderBy(static value => value.persistentId).ToArray();
    }

    private void ValidateImportDependenciesLocked(AssetMeta candidate)
    {
        string node = candidate.relativePath;
        IReadOnlyList<string> previous = m_importGraph.GetDependencies(node);
        string[] dependencies = candidate.importDependencies
            .Where(static value => (AssetImportDependencyKind)value.kind == AssetImportDependencyKind.Source)
            .Select(static value => value.key)
            .ToArray();
        m_importGraph.ReplaceDependencies(node, dependencies);
        if (m_importGraph.TryFindCycle(out IReadOnlyList<string> cycle))
        {
            m_importGraph.ReplaceDependencies(node, previous);
            throw new InvalidOperationException(
                $"Asset import dependency cycle detected: {string.Join(" -> ", cycle)}.");
        }
    }

    private void ValidateImportDependencySnapshotsLocked(AssetMeta candidate)
    {
        for (int i = 0; i < candidate.importDependencies.Length; i++)
        {
            AssetImportDependencyData dependency = candidate.importDependencies[i];
            string fingerprint = ComputeImportDependencyFingerprintLocked(
                ref dependency,
                out bool metadataChanged);
            if (!string.Equals(dependency.fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"Import dependency '{dependency.key}' changed while " +
                    $"'{candidate.relativePath}' was importing.");
            }
            if (metadataChanged)
                candidate.importDependencies[i] = dependency;
        }
    }

    private void RescanLocked()
    {
        LoadCatalogLocked();
        bool releasedRetiredAssets = ReleaseRetiredCanonicalAssetsLocked();
        if (m_runtimeArtifactsOnly)
        {
            ValidateRuntimeArtifactsLocked();
            if (releasedRetiredAssets)
                RefreshLoadedAssetReferencesLocked();
            m_importerRegistryVersion = m_importers.snapshotVersion;
            m_buildProcessorRegistryVersion = m_buildProcessors.snapshotVersion;
            return;
        }
        EnsureDirectoryMetadataLocked();
        AssetPath[] sourceFiles = m_mounts.Values
            .SelectMany(mount => Directory.GetFiles(mount.rootPath, "*", SearchOption.AllDirectories)
                .Select(path => new AssetPath(
                    mount.id,
                    Path.GetRelativePath(mount.rootPath, path).Replace('\\', '/'))))
            .Where(path => !IsSourceIgnored(path.localPath, isDirectory: false))
            .ToArray();
        foreach (AssetPath sourceFile in sourceFiles)
        {
            string relative = sourceFile.ToString();
            string absoluteSource = GetSourcePath(relative);
            AssetImporter? importer = m_importers.FindByPath(relative);
            if (importer is null)
            {
                TrackUnsupportedSourceLocked(relative);
                continue;
            }
            TryAssociateUntrackedRenameLocked(relative, absoluteSource);
            AssetRecord? record = FindRecordLocked(relative);
            if (record is null ||
                record.asset?.isMissing == true ||
                IsStale(record, out _) ||
                !m_artifacts.TryGet(
                    new AssetArtifactKey(record.meta.artifactKey),
                    "asset-state",
                    out _))
            {
                ImportLocked(relative);
            }
        }

        foreach (AssetRecord record in m_recordsByPath.Values.ToArray())
        {
            if (!IsMounted(record.relativePath))
            {
                RetireUnmountedRecordLocked(record);
                continue;
            }

            if (IOFile.Exists(GetSourcePath(record.relativePath)) ||
                Directory.Exists(GetSourcePath(record.relativePath)))
                continue;
            HandleDeletedLocked(record.relativePath);
        }
        if (releasedRetiredAssets)
            RefreshLoadedAssetReferencesLocked();
        CommitCatalogLocked();
        EnsureReadOnlyImportsSucceededLocked();
        m_importerRegistryVersion = m_importers.snapshotVersion;
        m_buildProcessorRegistryVersion = m_buildProcessors.snapshotVersion;
    }

    private AssetRuntimeContentInfo ExportRuntimeArtifactsLocked(
        string destinationLibraryRoot,
        CancellationToken cancellationToken,
        SerializationGeneration? serialization = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLibraryRoot);
        string destination = Path.GetFullPath(destinationLibraryRoot);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Runtime asset content destination must be empty.");
        Directory.CreateDirectory(destination);

        AssetRecord[] imported = m_recordsByPath.Values
            .Where(static record =>
                !record.meta.isDirectory
                && !record.meta.isTombstone
                && record.meta.importStatus == (int)AssetImportStatus.Imported)
            .OrderBy(static record => record.relativePath, StringComparer.Ordinal)
            .ToArray();
        AssetRecord? invalidScope = imported.FirstOrDefault(static record =>
            !Enum.IsDefined((AssetDeploymentScope)record.meta.deploymentScope));
        if (invalidScope is not null)
        {
            throw new InvalidOperationException(
                $"Asset '{invalidScope.relativePath}' has an invalid deployment scope.");
        }
        AssetRecord[] exported = imported
            .Where(static record => record.meta.deploymentScope == (int)AssetDeploymentScope.Runtime)
            .ToArray();
        string[] keys = exported
            .Select(static record => record.meta.artifactKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (AssetRecord record in exported)
        {
            AssetArtifactKey key = new(record.meta.artifactKey);
            if (!m_artifacts.TryGet(key, "asset-state", serialization, out _)
                || !m_artifacts.TryGet(key, "runtime", serialization, out _))
            {
                throw new InvalidOperationException(
                    $"Asset '{record.relativePath}' has no complete runtime artifact bundle '{key}'.");
            }
        }
        HashSet<Guid> exportedIds = exported
            .Select(static record => record.persistentId)
            .ToHashSet();
        foreach (AssetRecord record in exported)
        {
            AssetDependencyData unavailable = record.meta.runtimeDependencies
                .FirstOrDefault(dependency => !exportedIds.Contains(dependency.persistentId));
            if (unavailable.persistentId != Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Runtime asset '{record.relativePath}' depends on non-runtime asset " +
                    $"'{unavailable.lastKnownPath}'.");
            }
        }

        AssetCatalogStore catalog = serialization is null
            ? new AssetCatalogStore(destination, m_serialization)
            : new AssetCatalogStore(destination, serialization);
        catalog.Commit(exported.Select(static record => record.meta).ToArray());
        long totalBytes = Directory
            .EnumerateFiles(Path.Combine(destination, "AssetDatabase"), "*", SearchOption.AllDirectories)
            .Sum(static path => new FileInfo(path).Length);
        foreach (string keyValue in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetArtifactKey key = new(keyValue);
            string source = GetArtifactBundlePath(m_artifacts.root, key);
            string target = GetArtifactBundlePath(Path.Combine(destination, "Artifacts"), key);
            totalBytes += CopyDirectory(source, target, cancellationToken);
        }

        AssetSourceId[] sources = exported
            .Select(static record => AssetPath.Parse(record.relativePath).source)
            .Append(AssetSourceId.project)
            .Distinct()
            .OrderBy(static source => source.value, StringComparer.Ordinal)
            .ToArray();
        foreach (AssetSourceId source in sources)
            Directory.CreateDirectory(Path.Combine(destination, "Sources", source.value));
        return new AssetRuntimeContentInfo(sources, exported.Length, keys.Length, totalBytes);
    }

    private static string GetArtifactBundlePath(string root, AssetArtifactKey key)
    {
        string value = key.value;
        if (value.Length < 4)
            throw new InvalidDataException("A deployed artifact key must contain a SHA-256 value.");
        return Path.Combine(root, value[..2].ToLowerInvariant(), value[2..4].ToLowerInvariant(), value);
    }

    private static long CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Artifact bundle '{source}' does not exist.");
        long bytes = 0;
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            IOFile.Copy(file, target, overwrite: false);
            bytes = checked(bytes + new FileInfo(file).Length);
        }
        return bytes;
    }

    private void ValidateRuntimeArtifactsLocked()
    {
        foreach (AssetRecord record in m_recordsByPath.Values)
        {
            if (!IsMounted(record.relativePath))
            {
                throw new InvalidDataException(
                    $"Runtime asset '{record.relativePath}' references an undeclared source mount.");
            }
            if (record.meta.isDirectory || record.meta.isTombstone)
                continue;
            if (record.meta.deploymentScope != (int)AssetDeploymentScope.Runtime)
            {
                throw new InvalidDataException(
                    $"Runtime catalog contains authoring-only asset '{record.relativePath}'.");
            }
            if (record.meta.importStatus != (int)AssetImportStatus.Imported)
            {
                throw new InvalidDataException(
                    $"Runtime asset '{record.relativePath}' was not successfully imported before export.");
            }
            AssetArtifactKey key = new(record.meta.artifactKey);
            if (!m_artifacts.TryGet(key, "asset-state", out _)
                || !m_artifacts.TryGet(key, "runtime", out _))
            {
                throw new InvalidDataException(
                    $"Runtime asset '{record.relativePath}' has an incomplete artifact bundle '{key}'.");
            }
        }
    }

    private void EnsureReadOnlyImportsSucceededLocked()
    {
        AssetRecord[] failed = m_recordsByPath.Values
            .Where(record =>
                GetMount(record.relativePath).isReadOnly
                && record.meta.importStatus is (int)AssetImportStatus.Failed
                    or (int)AssetImportStatus.Conflict)
            .OrderBy(static record => record.relativePath, StringComparer.Ordinal)
            .ToArray();
        if (failed.Length == 0)
        {
            return;
        }

        string details = string.Join(
            "; ",
            failed.Select(record =>
                $"{record.relativePath}: {string.Join(" | ", record.meta.diagnostics)}"));
        throw new InvalidDataException(
            $"Read-only Asset Source candidate contains failed imports: {details}");
    }

    private bool IsMounted(string canonicalPath)
        => m_mounts.ContainsKey(AssetPath.Parse(canonicalPath).source);

    private void RetireUnmountedRecordLocked(AssetRecord record)
    {
        string recordPath = record.relativePath;
        m_recordsByPath.Remove(recordPath);
        m_importGraph.RemoveNode(recordPath);
        if (record.persistentId == Guid.Empty)
        {
            return;
        }

        record.meta.isTombstone = true;
        record.meta.importStatus = (int)AssetImportStatus.Missing;
        record.meta.diagnostics = [$"Asset source mount for '{recordPath}' is not active."];
        record.meta.artifactKey = string.Empty;
        record.meta.lastSuccessfulArtifactKey = string.Empty;
        record.meta.assetStateBytes = [];
        record.meta.runtimeDependencies = [];
        record.meta.importDependencies = [];
        record.payload = [];
        m_runtimeGraph.ReplaceDependencies(record.persistentId, []);
        if (record.asset is null)
        {
            return;
        }

        m_dependencyRetention.Remove(record.asset);
        AssetRuntimeHost.Initialize(
            record.asset,
            AssetPath.Parse(recordPath),
            record.meta.sourceHash,
            ReadOnlyMemory<byte>.Empty,
            true,
            record.asset.contentVersion + 1);
        PublishReloaded(record.asset);
    }

    private bool ReleaseRetiredCanonicalAssetsLocked()
    {
        bool releasedAny = false;
        foreach (AssetRecord record in m_recordsByPath.Values.ToArray())
        {
            AssetObject? asset = record.asset;
            if (asset is null)
                continue;
            bool isCurrent = m_types.TryGetTypeRef(asset.GetType(), out _);
            TypeRef persistedType = new(record.stableTypeId);
            if (isCurrent &&
                (record.stableTypeId == Guid.Empty ||
                 !m_types.TryResolve(persistedType, out Type? resolvedType) ||
                 resolvedType == asset.GetType()))
            {
                continue;
            }

            m_dependencyRetention.Remove(asset);
            AssetRuntimeHost.Release(asset);
            releasedAny = true;
            try
            {
                _ = m_identities.Unregister(asset);
            }
            catch (Exception exception)
            {
                m_log.Write(
                    LogLevel.Error,
                    "Retired asset '{0}' was released, but an identity observer failed: {1}",
                    [record.relativePath, exception]);
            }
            finally
            {
                record.asset = null;
                PublishReloaded(asset);
            }
        }
        return releasedAny;
    }

    private void RefreshLoadedAssetReferencesLocked()
    {
        AssetRecord[] loaded = m_recordsByPath.Values
            .Where(static record => record.asset is not null)
            .ToArray();
        for (int i = 0; i < loaded.Length; i++)
        {
            AssetRecord record = loaded[i];
            AssetObject asset = record.asset!;
            if (record.meta.assetStateBytes.Length == 0)
                continue;
            byte[] rollback = CaptureAssetState(asset);
            try
            {
                RestoreAssetState(asset, record.meta.assetStateBytes);
            }
            catch
            {
                RestoreAssetState(asset, rollback);
                throw;
            }
        }
        for (int i = 0; i < loaded.Length; i++)
            AttachDependenciesLocked(loaded[i]);
    }

    private void LoadCatalogLocked()
    {
        AssetMeta[] catalogEntries;
        try
        {
            catalogEntries = m_catalog.Load();
        }
        catch (Exception exception)
        {
            if (m_diagnostics.PublishCatalogFailure(exception))
                m_log.Write(LogLevel.Error, "Asset catalog load failed: {0}", [exception]);
            throw;
        }
        for (int i = 0; i < catalogEntries.Length; i++)
            MergeCatalogMetaLocked(catalogEntries[i]);

        foreach (AssetSourceMount mount in m_mounts.Values)
        {
            foreach (string metaPath in Directory.GetFiles(
                         mount.rootPath,
                         "*" + C_META_POSTFIX,
                         SearchOption.AllDirectories))
            {
                string localMeta = Path.GetRelativePath(mount.rootPath, metaPath).Replace('\\', '/');
                string relative = new AssetPath(
                    mount.id,
                    localMeta[..^C_META_POSTFIX.Length]).ToString();
                if (Directory.Exists(GetSourcePath(relative)))
                    continue;
                try
                {
                    AssetSourceMeta sourceMeta = m_serialization.Deserialize<AssetSourceMeta>(
                        IOFile.ReadAllBytes(metaPath));
                    if (sourceMeta.persistentId != Guid.Empty)
                    {
                        AssetRecord? existing = FindRecordByIdWithoutLoading(sourceMeta.persistentId);
                        if (existing is not null &&
                            !string.Equals(existing.relativePath, relative, StringComparison.OrdinalIgnoreCase) &&
                            IOFile.Exists(GetSourcePath(existing.relativePath)))
                        {
                            if (mount.isReadOnly)
                            {
                                throw new InvalidDataException(
                                    $"Read-only source '{relative}' duplicates persistent ID '{sourceMeta.persistentId}'.");
                            }
                            sourceMeta.persistentId = Guid.NewGuid();
                            WriteAtomic(metaPath, m_serialization.Serialize(sourceMeta));
                        }
                        // Index every source identity before importing source bodies. Runtime
                        // dependencies are identity-first, so import order must not decide whether
                        // a relocated asset can resolve another asset in the same mount snapshot.
                        _ = FindRecordLocked(relative);
                        continue;
                    }
                }
                catch when (!mount.isReadOnly)
                {
                    // Writable corrupt sidecars remain visible as catalog diagnostics.
                }
            }
        }
    }

    private void ApplySourceChangesLocked(IReadOnlyList<AssetChangedEvent> changes)
    {
        IReadOnlyDictionary<string, int> ambiguousRenames = AssociateUntrackedRenamesLocked(changes);
        foreach (AssetChangedEvent change in changes)
        {
            if (IsInternalGeneratedPath(change.relativePath))
                continue;
            if (change.changeType.HasFlag(WatcherChangeTypes.Renamed))
            {
                HandleRenameLocked(change.oldRelativePath, change.relativePath);
                continue;
            }
            if (change.changeType.HasFlag(WatcherChangeTypes.Deleted))
            {
                HandleDeletedLocked(change.relativePath);
                continue;
            }
            ImportLocked(NormalizeRelativePath(change.relativePath));
        }
        foreach ((string path, int matchCount) in ambiguousRenames)
            RecordAmbiguousRenameDiagnosticLocked(path, matchCount);
    }

    private void HandleRenameLocked(string oldPath, string newPath)
    {
        string oldNormalized = NormalizeRelativePath(oldPath);
        string newNormalized = NormalizeRelativePath(newPath);
        if (Directory.Exists(GetSourcePath(newNormalized)))
        {
            HandleDirectoryRenameLocked(oldNormalized, newNormalized);
            return;
        }
        AssetRecord? record = FindRecordLocked(oldNormalized);
        if (record is null)
        {
            ImportLocked(newNormalized);
            return;
        }
        string oldMeta = GetMetaPath(oldNormalized);
        string newMeta = GetMetaPath(newNormalized);
        if (TryReadSourceMeta(newMeta, out AssetSourceMeta targetMeta) &&
            targetMeta.persistentId != record.persistentId)
        {
            record.meta.importStatus = (int)AssetImportStatus.Conflict;
            record.meta.diagnostics =
            [
                $"Rename target '{newNormalized}' already owns persistent id " +
                $"'{targetMeta.persistentId}'."
            ];
            CommitCatalogLocked();
            return;
        }

        if (IOFile.Exists(oldMeta) && !IOFile.Exists(newMeta))
            MoveGeneratedFile(oldMeta, newMeta);
        m_recordsByPath.Remove(oldNormalized);
        m_importGraph.RemoveNode(oldNormalized);
        record.relativePath = newNormalized;
        record.meta.relativePath = newNormalized;
        AssetImporter? importer = m_importers.FindByPath(newNormalized);
        if (importer is null)
        {
            record.meta.importStatus = (int)AssetImportStatus.Unsupported;
            record.meta.diagnostics =
                [$"No importer supports '{Path.GetExtension(newNormalized)}'."];
            record.meta.importerId = string.Empty;
            record.meta.artifactKey = string.Empty;
            if (record.asset is not null)
            {
                AssetObject replaced = record.asset;
                AssetRuntimeHost.Initialize(
                    replaced,
                    AssetPath.Parse(newNormalized),
                    record.meta.sourceHash,
                    ReadOnlyMemory<byte>.Empty,
                    true,
                    replaced.contentVersion + 1);
                AssetRuntimeHost.Release(replaced);
                m_identities.Unregister(replaced);
                record.asset = null;
                PublishReloaded(replaced);
            }
        }
        else
        {
            record.meta.importStatus = (int)AssetImportStatus.Imported;
            record.meta.diagnostics = [];
            if (record.asset is not null)
                AssetRuntimeHost.UpdateAssetPath(record.asset, AssetPath.Parse(newNormalized));
        }
        m_recordsByPath[newNormalized] = record;
        WriteSourceMeta(record.meta);
        UpdateGraphsLocked(record);
        CommitCatalogLocked();

        bool catalogChanged = false;
        bool stale = importer is not null && IsStale(record, out catalogChanged);
        if (importer is not null &&
            (!string.Equals(importer.importerId, record.meta.importerId, StringComparison.Ordinal) ||
             stale))
        {
            ImportLocked(newNormalized);
        }
        else if (catalogChanged)
        {
            CommitCatalogLocked();
        }
    }

    private void HandleDeletedLocked(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        string prefix = normalized + "/";
        AssetRecord[] records = m_recordsByPath.Values
            .Where(record =>
                string.Equals(record.relativePath, normalized, StringComparison.OrdinalIgnoreCase) ||
                record.relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static record => record.relativePath.Length)
            .ToArray();
        if (records.Length == 0)
            return;

        for (int i = 0; i < records.Length; i++)
        {
            AssetRecord record = records[i];
            string recordPath = record.relativePath;
            DeleteIfExists(GetMetaPath(recordPath));
            m_recordsByPath.Remove(recordPath);
            m_importGraph.RemoveNode(recordPath);

            if (record.persistentId == Guid.Empty)
                continue;

            record.meta.isTombstone = true;
            record.meta.importStatus = (int)AssetImportStatus.Missing;
            record.meta.diagnostics = [$"Source '{recordPath}' was removed."];
            record.meta.artifactKey = string.Empty;
            record.meta.lastSuccessfulArtifactKey = string.Empty;
            record.meta.assetStateBytes = [];
            record.meta.runtimeDependencies = [];
            record.meta.importDependencies = [];
            record.payload = [];
            m_runtimeGraph.ReplaceDependencies(record.persistentId, []);
            if (record.asset is not null)
            {
                m_dependencyRetention.Remove(record.asset);
                AssetRuntimeHost.Initialize(
                    record.asset,
                    AssetPath.Parse(recordPath),
                    record.meta.sourceHash,
                    ReadOnlyMemory<byte>.Empty,
                    true,
                    record.asset.contentVersion + 1);
                PublishReloaded(record.asset);
            }
        }
        CommitCatalogLocked();
    }

    private IReadOnlyList<AssetDependency> GetDependenciesLocked(Guid persistentId, bool recursive)
    {
        if (!m_recordsById.TryGetValue(persistentId, out AssetRecord? record))
            return Array.Empty<AssetDependency>();
        IEnumerable<Guid> ids = recursive
            ? m_runtimeGraph.GetDependencies(persistentId, recursive: true)
            : m_runtimeGraph.GetDependencies(persistentId);
        return ids.Select(id => m_recordsById.TryGetValue(id, out AssetRecord? dependency)
                ? new AssetDependency(id, new TypeRef(dependency.stableTypeId), dependency.relativePath)
                : FindDescriptor(record.meta, id))
            .Where(static descriptor => descriptor.persistentId != Guid.Empty)
            .ToArray();
    }

    private AssetReferenceInfo GetReferenceInfoLocked(AssetObject asset)
    {
        Guid id = asset.identity.persistentId;
        var locations = new List<AssetReferenceLocation>();
        foreach (Guid dependentId in m_runtimeGraph.GetDependents(id))
        {
            if (!m_recordsById.TryGetValue(dependentId, out AssetRecord? dependent))
                continue;
            locations.Add(AssetRuntimeHost.CreateReferenceLocation(
                AssetReferenceKind.AssetDependency,
                dependentId,
                dependent.relativePath,
                "runtimeDependencies"));
        }
        m_recordsById.TryGetValue(id, out AssetRecord? record);
        return AssetRuntimeHost.CreateReferenceInfo(
            id,
            asset.assetPath,
            asset.contentVersion,
            record?.asset is not null,
            record?.lastSweepReachability,
            locations.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int UnloadUnusedAssetsLocked()
    {
        SweepCandidate[] candidates = DetachSweepCandidatesLocked();
        if (candidates.Length == 0)
            return 0;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        int released = 0;
        foreach (SweepCandidate candidate in candidates)
        {
            if (candidate.reference.TryGetTarget(out AssetObject? survivor))
            {
                candidate.record.asset = survivor;
                candidate.record.lastSweepReachability = true;
                continue;
            }
            candidate.record.lastSweepReachability = false;
            released++;
        }
        return released;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SweepCandidate[] DetachSweepCandidatesLocked()
    {
        var result = new List<SweepCandidate>();
        foreach (AssetRecord record in m_recordsByPath.Values)
        {
            if (record.asset is null)
                continue;
            result.Add(new SweepCandidate(record, new WeakReference<AssetObject>(record.asset)));
            record.asset = null;
        }
        return result.ToArray();
    }

    private void DisposeLocked()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        foreach (AssetRecord record in m_recordsByPath.Values)
        {
            if (record.asset is null)
                continue;
            AssetRuntimeHost.Release(record.asset);
            m_identities.Unregister(record.asset);
            record.asset = null;
        }
        m_recordsByPath.Clear();
        m_recordsById.Clear();
        m_runtimeGraph.Clear();
        m_importGraph.Clear();
        m_missingAssets.Clear();
        m_diagnostics.Dispose();
        m_importers.Dispose();
        m_buildProcessors.Dispose();
        lock (m_asyncSync)
        {
            m_inFlightPathLoads.Clear();
            m_inFlightIdLoads.Clear();
        }
    }

    private AssetRecord? FindRecordLocked(string relativePath)
    {
        if (m_recordsByPath.TryGetValue(relativePath, out AssetRecord? record))
            return record;
        string metaPath = GetMetaPath(relativePath);
        if (!TryReadSourceMeta(metaPath, out AssetSourceMeta sourceMeta))
            return null;
        if (sourceMeta.persistentId == Guid.Empty)
            return null;
        if (m_recordsById.TryGetValue(sourceMeta.persistentId, out AssetRecord? tombstone) &&
            tombstone.meta.isTombstone)
        {
            tombstone.relativePath = relativePath;
            tombstone.persistentId = sourceMeta.persistentId;
            tombstone.meta = new AssetMeta
            {
                relativePath = relativePath,
                persistentId = sourceMeta.persistentId,
                importerId = sourceMeta.importerId,
                importStatus = (int)AssetImportStatus.Pending
            };
            AddOrReplaceRecordLocked(tombstone);
            return tombstone;
        }
        if (m_recordsById.TryGetValue(sourceMeta.persistentId, out AssetRecord? sameId) &&
            !string.Equals(sameId.relativePath, relativePath, StringComparison.OrdinalIgnoreCase))
        {
            if (IOFile.Exists(GetSourcePath(sameId.relativePath)) ||
                Directory.Exists(GetSourcePath(sameId.relativePath)))
            {
                sourceMeta.persistentId = Guid.NewGuid();
                WriteAtomic(metaPath, m_serialization.Serialize(sourceMeta));
            }
            else
            {
                HandleRenameLocked(sameId.relativePath, relativePath);
                return sameId;
            }
        }
        record = m_recordsById.GetValueOrDefault(sourceMeta.persistentId) ?? new AssetRecord();
        record.relativePath = relativePath;
        record.persistentId = sourceMeta.persistentId;
        if (record.meta.isTombstone)
        {
            record.meta = new AssetMeta
            {
                relativePath = relativePath,
                persistentId = sourceMeta.persistentId,
                importerId = sourceMeta.importerId,
                importStatus = (int)AssetImportStatus.Pending
            };
        }
        AddOrReplaceRecordLocked(record);
        return record;
    }

    private void AddOrReplaceRecordLocked(AssetRecord record)
    {
        if (record.persistentId == Guid.Empty)
        {
            m_recordsByPath[record.relativePath] = record;
            return;
        }
        if (m_recordsById.TryGetValue(record.persistentId, out AssetRecord? sameId) &&
            !ReferenceEquals(sameId, record) &&
            !string.Equals(sameId.relativePath, record.relativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset persistent id '{record.persistentId}' is used by both " +
                $"'{sameId.relativePath}' and '{record.relativePath}'.");
        }
        m_recordsByPath[record.relativePath] = record;
        m_recordsById[record.persistentId] = record;
    }

    private void RemoveRecordLocked(AssetRecord record, bool removeGeneratedFiles = true)
    {
        m_recordsByPath.Remove(record.relativePath);
        if (record.persistentId != Guid.Empty)
        {
            m_recordsById.Remove(record.persistentId);
            m_runtimeGraph.RemoveNode(record.persistentId);
        }
        m_importGraph.RemoveNode(record.relativePath);
        if (removeGeneratedFiles)
            DeleteIfExists(GetMetaPath(record.relativePath));
    }

    private void UpdateGraphsLocked(AssetRecord record)
    {
        m_runtimeGraph.ReplaceDependencies(
            record.persistentId,
            record.meta.runtimeDependencies.Select(static value => value.persistentId));
        m_importGraph.ReplaceDependencies(
            record.relativePath,
            record.meta.importDependencies
                .Where(static value => (AssetImportDependencyKind)value.kind == AssetImportDependencyKind.Source)
                .Select(static value => value.key));
    }

    private AssetRecord? FindDependencyRecordLocked(AssetDependency dependency)
    {
        if (m_recordsById.TryGetValue(dependency.persistentId, out AssetRecord? record))
            return record;
        if (!string.IsNullOrWhiteSpace(dependency.lastKnownPath))
            return FindRecordLocked(NormalizeRelativePath(dependency.lastKnownPath));
        return null;
    }

    private Type? ResolveRecordType(AssetRecord? record)
    {
        if (record is null)
            return null;
        if (record.asset is not null)
            return record.asset.GetType();
        TypeRef typeRef = new(record.stableTypeId);
        return m_types.TryResolve(typeRef, out Type? type) && typeof(AssetObject).IsAssignableFrom(type)
            ? type
            : m_importers.FindById(record.meta.importerId)?.targetAssetType;
    }

    private Type ResolveDependencyExpectedType(AssetDependency dependency)
    {
        TypeRef typeRef = dependency.type;
        if (m_types.TryResolve(typeRef, out Type? type) && typeof(AssetObject).IsAssignableFrom(type))
        {
            return type;
        }
        return typeof(MissingAsset);
    }

    private bool IsStale(AssetRecord record, out bool catalogChanged)
    {
        catalogChanged = false;
        string sourcePath = GetSourcePath(record.relativePath);
        if (!IOFile.Exists(sourcePath))
            return true;
        AssetImporter? importer = m_importers.FindById(record.meta.importerId);
        if (importer is null ||
            !string.Equals(
                record.meta.importerImplementationFingerprint,
                GetImporterImplementationFingerprint(importer),
                StringComparison.Ordinal))
        {
            return true;
        }
        if (record.importerGeneration != m_importers.GetGeneration(record.meta.importerId))
            return true;
        if (record.meta.importStatus != (int)AssetImportStatus.Imported)
            return true;
        if (!AssetSourceFileStamp.TryCapture(sourcePath, out AssetSourceFileStamp sourceStamp))
            return true;
        if (!SourceStampMatches(record.meta, sourceStamp))
        {
            byte[] sourceBytes = ReadStableSourceBytes(sourcePath, out sourceStamp);
            if (!string.Equals(
                    record.meta.sourceHash,
                    ComputeSha256Hex(sourceBytes),
                    StringComparison.Ordinal))
            {
                return true;
            }
            ApplySourceStamp(record.meta, sourceStamp);
            catalogChanged = true;
        }
        for (int i = 0; i < record.meta.importDependencies.Length; i++)
        {
            AssetImportDependencyData dependency = record.meta.importDependencies[i];
            string fingerprint = ComputeImportDependencyFingerprintLocked(
                ref dependency,
                out bool dependencyChanged);
            if (dependencyChanged)
            {
                record.meta.importDependencies[i] = dependency;
                catalogChanged = true;
            }
            if (!string.Equals(
                    dependency.fingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void RollbackLoadLocked(AssetLoadTransaction transaction)
    {
        foreach (AssetRecord record in transaction.createdRecords.AsEnumerable().Reverse())
        {
            if (record.asset is not null)
            {
                AssetRuntimeHost.Release(record.asset);
                m_identities.Unregister(record.asset);
            }
            record.asset = null;
        }
    }

    private void RestoreCanonical(
        AssetObject canonical,
        byte[] state,
        byte[] payload,
        string sourcePath,
        string sourceHash,
        bool isMissing,
        long version)
    {
        RestoreAssetState(canonical, state);
        AssetRuntimeHost.Initialize(
            canonical,
            AssetPath.Parse(sourcePath),
            sourceHash,
            payload,
            isMissing,
            version);
    }

    private void PublishReloaded(AssetObject asset)
    {
        Action<AssetObject>? handlers = AssetReloaded;
        if (handlers is null)
            return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<AssetObject>)handler)(asset);
            }
            catch
            {
                // Observer failures cannot roll back an already committed asset transaction.
            }
        }
    }

    private T Execute<T>(Func<T> action, bool allowDisposed = false)
    {
        if (ReferenceEquals(t_activeLoader, this))
            return action();
        if (m_disposed && !allowDisposed)
            throw new ObjectDisposedException(nameof(AssetLoader));
        m_operationGate.Wait();
        AssetLoader? previous = t_activeLoader;
        t_activeLoader = this;
        try
        {
            if (m_disposed && !allowDisposed)
                throw new ObjectDisposedException(nameof(AssetLoader));
            return action();
        }
        finally
        {
            t_activeLoader = previous;
            m_operationGate.Release();
        }
    }

    private void Execute(Action action, bool allowDisposed = false)
        => Execute(() =>
        {
            action();
            return 0;
        }, allowDisposed);

    private static async ValueTask<AssetObject?> AwaitSharedLoad(
        Task<AssetObject?> operation,
        Type requestedAssetType,
        CancellationToken cancellationToken)
    {
        AssetObject? asset = await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        return asset is not null && requestedAssetType.IsInstanceOfType(asset) ? asset : null;
    }

    private void RemovePathOperation(string relativePath, Task<AssetObject?> operation)
    {
        lock (m_asyncSync)
        {
            if (m_inFlightPathLoads.TryGetValue(relativePath, out Task<AssetObject?>? current) &&
                ReferenceEquals(current, operation))
            {
                m_inFlightPathLoads.Remove(relativePath);
            }
        }
    }

    private void RemoveIdOperation(Guid persistentId, Task<AssetObject?> operation)
    {
        lock (m_asyncSync)
        {
            if (m_inFlightIdLoads.TryGetValue(persistentId, out Task<AssetObject?>? current) &&
                ReferenceEquals(current, operation))
            {
                m_inFlightIdLoads.Remove(persistentId);
            }
        }
    }

    private string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("An asset-relative path is required.", nameof(relativePath));
        AssetPath path = AssetPath.Parse(relativePath);
        _ = GetMount(path);
        return path.ToString();
    }

    private string NormalizeAssetPath(AssetPath path)
    {
        if (!path.isValid || string.IsNullOrWhiteSpace(path.localPath))
            throw new ArgumentException("An isolated asset path is required.", nameof(path));
        _ = GetMount(path);
        return path.ToString();
    }

    private bool TryNormalizeCatalogPath(AssetPath path, out string normalized)
    {
        if (!path.isValid)
            throw new ArgumentException("A valid isolated asset path is required.", nameof(path));
        _ = GetMount(path);
        if (string.IsNullOrWhiteSpace(path.localPath))
        {
            normalized = string.Empty;
            return false;
        }
        normalized = path.ToString();
        return true;
    }

    private AssetInfo? CreateInfo(AssetRecord? record)
    {
        if (record is null)
            return null;
        return new AssetInfo(
            record.persistentId,
            AssetPath.Parse(record.relativePath),
            record.meta.isDirectory ? AssetSourceKind.Directory : AssetSourceKind.File,
            Enum.IsDefined(typeof(AssetImportStatus), record.meta.importStatus)
                ? (AssetImportStatus)record.meta.importStatus
                : AssetImportStatus.Failed,
            record.meta.importerId,
            record.stableTypeId,
            new AssetArtifactKey(record.meta.artifactKey),
            new AssetArtifactKey(record.meta.lastSuccessfulArtifactKey),
            record.meta.diagnostics);
    }

    private void TrackUnsupportedSourceLocked(string relativePath)
    {
        AssetRecord record = m_recordsByPath.GetValueOrDefault(relativePath) ?? new AssetRecord();
        if (record.persistentId != Guid.Empty)
            m_recordsById.Remove(record.persistentId);
        record.relativePath = relativePath;
        record.persistentId = Guid.Empty;
        record.stableTypeId = Guid.Empty;
        record.meta.relativePath = relativePath;
        record.meta.persistentId = Guid.Empty;
        record.meta.importerId = string.Empty;
        record.meta.stableAssetTypeId = Guid.Empty;
        record.meta.importStatus = (int)AssetImportStatus.Unsupported;
        record.meta.diagnostics = [$"No importer supports '{Path.GetExtension(relativePath)}'."];
        record.meta.artifactKey = string.Empty;
        m_recordsByPath[relativePath] = record;
    }

    private void CommitCatalogLocked()
    {
        AssetMeta[] entries = m_recordsById.Values
            .Concat(m_recordsByPath.Values.Where(static record => record.persistentId == Guid.Empty))
            .Distinct()
            .Select(static record => record.meta)
            .OrderBy(static meta => meta.relativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        try
        {
            m_catalog.Commit(entries);
            m_diagnostics.ResolveCatalog();
        }
        catch (Exception exception)
        {
            if (m_diagnostics.PublishCatalogFailure(exception))
                m_log.Write(LogLevel.Error, "Asset catalog commit failed: {0}", [exception]);
            throw;
        }
        m_diagnostics.SynchronizeImports(entries);
    }

    private void MergeCatalogMetaLocked(AssetMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.relativePath))
            return;
        if (meta.isTombstone)
        {
            if (meta.persistentId == Guid.Empty)
                return;
            AssetRecord tombstone = FindRecordByIdWithoutLoading(meta.persistentId) ?? new AssetRecord();
            if (!string.IsNullOrWhiteSpace(tombstone.relativePath))
                m_recordsByPath.Remove(tombstone.relativePath);
            tombstone.relativePath = meta.relativePath;
            tombstone.persistentId = meta.persistentId;
            tombstone.stableTypeId = meta.stableAssetTypeId;
            tombstone.meta = meta;
            tombstone.payload = [];
            m_recordsById[meta.persistentId] = tombstone;
            return;
        }
        AssetRecord record = meta.persistentId == Guid.Empty
            ? m_recordsByPath.GetValueOrDefault(meta.relativePath) ?? new AssetRecord()
            : FindRecordByIdWithoutLoading(meta.persistentId) ?? new AssetRecord();
        if (!string.IsNullOrWhiteSpace(record.relativePath) &&
            !string.Equals(record.relativePath, meta.relativePath, StringComparison.OrdinalIgnoreCase))
        {
            m_recordsByPath.Remove(record.relativePath);
        }
        record.relativePath = meta.relativePath;
        record.persistentId = meta.persistentId;
        record.stableTypeId = meta.stableAssetTypeId;
        record.meta = meta;
        record.payload = m_artifacts.Read(new AssetArtifactKey(meta.artifactKey), "runtime");
        record.importerGeneration = m_importers.GetGeneration(meta.importerId);
        AddOrReplaceRecordLocked(record);
        if (record.persistentId != Guid.Empty)
            UpdateGraphsLocked(record);
    }

    private AssetRecord? FindRecordByIdWithoutLoading(Guid persistentId)
        => m_recordsById.TryGetValue(persistentId, out AssetRecord? record) ? record : null;

    private void EnsureDirectoryMetadataLocked()
    {
        foreach (AssetSourceMount mount in m_mounts.Values)
        {
            foreach (string directoryPath in Directory.GetDirectories(
                         mount.rootPath,
                         "*",
                         SearchOption.AllDirectories))
            {
            string localPath = Path.GetRelativePath(mount.rootPath, directoryPath).Replace('\\', '/');
            string relativePath = new AssetPath(mount.id, localPath).ToString();
            if (IsSourceIgnored(localPath, isDirectory: true))
                continue;
            string metaPath = GetMetaPath(relativePath);
            AssetSourceMeta sourceMeta;
            if (!TryReadSourceMeta(metaPath, out sourceMeta!))
            {
                if (mount.isReadOnly)
                {
                    throw new InvalidDataException(
                        $"Read-only source directory '{relativePath}' requires a valid '{C_META_POSTFIX}' sidecar.");
                }
                sourceMeta = new AssetSourceMeta
                {
                    persistentId = Guid.NewGuid(),
                    sourceKind = (int)AssetSourceKind.Directory
                };
                WriteAtomic(metaPath, m_serialization.Serialize(sourceMeta));
            }

            AssetRecord? record = FindRecordByIdWithoutLoading(sourceMeta.persistentId);
            if (record is not null &&
                !string.Equals(record.relativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(GetSourcePath(record.relativePath)))
            {
                if (mount.isReadOnly)
                {
                    throw new InvalidDataException(
                        $"Read-only source directory '{relativePath}' duplicates persistent ID '{sourceMeta.persistentId}'.");
                }
                sourceMeta.persistentId = Guid.NewGuid();
                WriteAtomic(metaPath, m_serialization.Serialize(sourceMeta));
                record = null;
            }
            record ??= new AssetRecord();
            record.relativePath = relativePath;
            record.persistentId = sourceMeta.persistentId;
            record.meta.relativePath = relativePath;
            record.meta.persistentId = sourceMeta.persistentId;
            record.meta.isDirectory = true;
            record.meta.isTombstone = false;
            record.meta.importStatus = (int)AssetImportStatus.Imported;
            record.meta.diagnostics = [];
            AddOrReplaceRecordLocked(record);
            }
        }
    }

    private int TryAssociateUntrackedRenameLocked(string relativePath, string absoluteSourcePath)
    {
        if (m_recordsByPath.ContainsKey(relativePath) || IOFile.Exists(GetMetaPath(relativePath)))
            return 0;
        string fingerprint = ComputeSha256Hex(IOFile.ReadAllBytes(absoluteSourcePath));
        AssetRecord[] matches = m_recordsByPath.Values
            .Where(record =>
                !record.meta.isDirectory &&
                !IOFile.Exists(GetSourcePath(record.relativePath)) &&
                string.Equals(record.meta.sourceHash, fingerprint, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 1)
            HandleRenameLocked(matches[0].relativePath, relativePath);
        return matches.Length;
    }

    private IReadOnlyDictionary<string, int> AssociateUntrackedRenamesLocked(
        IReadOnlyList<AssetChangedEvent> changes)
    {
        var ambiguous = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < changes.Count; i++)
        {
            AssetChangedEvent change = changes[i];
            if (change.changeType.HasFlag(WatcherChangeTypes.Deleted) ||
                change.changeType.HasFlag(WatcherChangeTypes.Renamed) ||
                IsInternalGeneratedPath(change.relativePath))
            {
                continue;
            }

            string relativePath = NormalizeRelativePath(change.relativePath);
            string absolutePath = GetSourcePath(relativePath);
            if (IOFile.Exists(absolutePath))
            {
                int matches = TryAssociateUntrackedRenameLocked(relativePath, absolutePath);
                if (matches > 1)
                    ambiguous[relativePath] = matches;
            }
        }
        return ambiguous;
    }

    private void RecordAmbiguousRenameDiagnosticLocked(string relativePath, int matchCount)
    {
        AssetRecord? record = FindRecordLocked(relativePath);
        if (record is null)
            return;
        string diagnostic =
            $"Warning: source '{relativePath}' matched {matchCount} removed assets; " +
            "a new persistent identity was assigned instead of guessing a rename.";
        record.meta.diagnostics = record.meta.diagnostics
            .Append(diagnostic)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CommitCatalogLocked();
    }

    private void HandleDirectoryRenameLocked(string oldPath, string newPath)
    {
        string oldPrefix = oldPath + "/";
        AssetRecord[] records = m_recordsByPath.Values
            .Where(record =>
                string.Equals(record.relativePath, oldPath, StringComparison.OrdinalIgnoreCase) ||
                record.relativePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static record => record.relativePath.Length)
            .ToArray();
        if (records.Length == 0)
        {
            EnsureDirectoryMetadataLocked();
            CommitCatalogLocked();
            return;
        }

        string oldFolderMeta = GetMetaPath(oldPath);
        string newFolderMeta = GetMetaPath(newPath);
        if (IOFile.Exists(oldFolderMeta) && !IOFile.Exists(newFolderMeta))
            MoveGeneratedFile(oldFolderMeta, newFolderMeta);

        for (int i = 0; i < records.Length; i++)
        {
            AssetRecord record = records[i];
            string suffix = record.relativePath.Length == oldPath.Length
                ? string.Empty
                : record.relativePath[oldPath.Length..];
            string destination = newPath + suffix;
            m_recordsByPath.Remove(record.relativePath);
            m_importGraph.RemoveNode(record.relativePath);
            record.relativePath = destination;
            record.meta.relativePath = destination;
            record.meta.importStatus = (int)AssetImportStatus.Imported;
            record.meta.diagnostics = [];
            if (record.asset is not null)
                AssetRuntimeHost.UpdateAssetPath(record.asset, AssetPath.Parse(destination));
            m_recordsByPath[destination] = record;
            UpdateGraphsLocked(record);
        }
        CommitCatalogLocked();
    }

    private void WriteSourceMeta(AssetMeta meta)
    {
        string metaPath = GetMetaPath(meta.relativePath);
        _ = TryReadSourceMeta(metaPath, out AssetSourceMeta existing);
        var sourceMeta = new AssetSourceMeta
        {
            persistentId = meta.persistentId,
            sourceKind = meta.isDirectory
                ? (int)AssetSourceKind.Directory
                : (int)AssetSourceKind.File,
            importerId = meta.importerId,
            importerSettingsBytes = existing?.importerSettingsBytes ?? []
        };
        byte[] data = m_serialization.Serialize(sourceMeta);
        if (GetMount(meta.relativePath).isReadOnly)
        {
            if (!IOFile.Exists(metaPath) || !IOFile.ReadAllBytes(metaPath).AsSpan().SequenceEqual(data))
            {
                throw new InvalidDataException(
                    $"Read-only source metadata for '{meta.relativePath}' is missing or inconsistent.");
            }
            return;
        }
        WriteAtomic(metaPath, data);
    }

    private bool TryReadSourceMeta(string metaPath, out AssetSourceMeta sourceMeta)
    {
        sourceMeta = null!;
        if (!IOFile.Exists(metaPath))
            return false;
        try
        {
            sourceMeta = m_serialization.Deserialize<AssetSourceMeta>(IOFile.ReadAllBytes(metaPath));
            return sourceMeta.persistentId != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    private string CreateImportFingerprint(AssetMeta meta)
    {
        var parts = new List<string>
        {
            "Inno.AssetImport",
            meta.sourceHash,
            meta.importerId,
            meta.importerImplementationFingerprint
        };
        foreach (AssetDependencyData dependency in meta.runtimeDependencies
                     .OrderBy(static value => value.persistentId))
        {
            parts.Add(dependency.persistentId.ToString("D"));
            if (m_recordsById.TryGetValue(dependency.persistentId, out AssetRecord? record))
                parts.Add(record.meta.artifactKey);
        }
        foreach (AssetImportDependencyData dependency in meta.importDependencies
                     .OrderBy(static value => value.kind)
                     .ThenBy(static value => value.key, StringComparer.Ordinal))
        {
            parts.Add(dependency.kind.ToString());
            parts.Add(dependency.key);
            parts.Add(dependency.fingerprint);
        }
        return string.Join("\n", parts);
    }

    private static string GetImporterImplementationFingerprint(AssetImporter importer)
        => $"{importer.GetType().Assembly.ManifestModule.ModuleVersionId:D}:" +
           $"{importer.GetType().FullName}:{importer.deploymentScope}";

    private string CreateBuildFingerprint(
        AssetBuildProcessor processor,
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs)
    {
        var parts = new List<string>
        {
            "Inno.AssetBuild",
            processor.processorId,
            processor.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D"),
            definition.identity.persistentId.ToString("D"),
            definition.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D"),
            definition.GetType().FullName ?? definition.GetType().Name,
            AssetRuntimeHost.GetSourceHash(definition),
            ComputeSha256Hex(CaptureAssetState(definition)),
            ComputeSha256Hex(definition.runtimePayload.ToArray())
        };
        foreach (AssetInfo input in inputs.OrderBy(static value => value.persistentId))
        {
            parts.Add(input.persistentId.ToString("D"));
            parts.Add(input.artifactKey.value);
        }
        return string.Join("\n", parts);
    }

    private string GetSourcePath(string relativePath)
    {
        AssetPath path = AssetPath.Parse(relativePath);
        return GetMount(path).Resolve(path.localPath);
    }

    private AssetSourceMount GetMount(string canonicalPath) => GetMount(AssetPath.Parse(canonicalPath));

    private AssetSourceMount GetMount(AssetPath path)
        => m_mounts.TryGetValue(path.source, out AssetSourceMount? mount)
            ? mount
            : throw new ArgumentException($"Asset source mount '{path.source}' is not active.", nameof(path));

    private string GetMetaPath(string relativePath) => GetSourcePath(relativePath) + C_META_POSTFIX;

    private bool IsSourceIgnored(string relativePath, bool isDirectory)
    {
        string localPath = AssetPath.Parse(relativePath).localPath;
        string[] segments = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - (isDirectory ? 0 : 1); i++)
        {
            if (m_sourcePolicy.IsIgnored(segments[i], isDirectory: true))
                return true;
        }
        return m_sourcePolicy.IsIgnored(localPath, isDirectory);
    }

    private static byte[] ReadStableSourceBytes(
        string sourcePath,
        out AssetSourceFileStamp sourceStamp)
    {
        const int C_MAX_ATTEMPTS = 3;
        for (int attempt = 0; attempt < C_MAX_ATTEMPTS; attempt++)
        {
            if (!AssetSourceFileStamp.TryCapture(sourcePath, out AssetSourceFileStamp before))
                throw new FileNotFoundException("Asset source is unavailable.", sourcePath);
            byte[] bytes = IOFile.ReadAllBytes(sourcePath);
            if (AssetSourceFileStamp.TryCapture(sourcePath, out AssetSourceFileStamp after) &&
                before == after &&
                bytes.LongLength == after.length)
            {
                sourceStamp = after;
                return bytes;
            }
        }

        throw new IOException($"Asset source '{sourcePath}' did not remain stable while it was read.");
    }

    private static bool SourceStampMatches(AssetMeta meta, AssetSourceFileStamp sourceStamp)
        => sourceStamp.isValid &&
           meta.sourceLength == sourceStamp.length &&
           meta.sourceLastWriteUtcTicks == sourceStamp.lastWriteUtcTicks &&
           meta.sourceCreationTimeUtcTicks == sourceStamp.creationTimeUtcTicks;

    private static bool SourceStampMatches(
        AssetImportDependencyData dependency,
        AssetSourceFileStamp sourceStamp)
        => dependency.sourceStampValid &&
           sourceStamp.isValid &&
           dependency.sourceLength == sourceStamp.length &&
           dependency.sourceLastWriteUtcTicks == sourceStamp.lastWriteUtcTicks &&
           dependency.sourceCreationTimeUtcTicks == sourceStamp.creationTimeUtcTicks;

    private static void ApplySourceStamp(AssetMeta meta, AssetSourceFileStamp sourceStamp)
    {
        if (!sourceStamp.isValid)
        {
            meta.sourceLength = -1;
            meta.sourceLastWriteUtcTicks = 0;
            meta.sourceCreationTimeUtcTicks = 0;
            return;
        }

        meta.sourceLength = sourceStamp.length;
        meta.sourceLastWriteUtcTicks = sourceStamp.lastWriteUtcTicks;
        meta.sourceCreationTimeUtcTicks = sourceStamp.creationTimeUtcTicks;
    }

    private static void ApplySourceStamp(
        ref AssetImportDependencyData dependency,
        AssetSourceFileStamp sourceStamp)
    {
        dependency.sourceStampValid = sourceStamp.isValid;
        dependency.sourceLength = sourceStamp.length;
        dependency.sourceLastWriteUtcTicks = sourceStamp.lastWriteUtcTicks;
        dependency.sourceCreationTimeUtcTicks = sourceStamp.creationTimeUtcTicks;
    }

    private static string ComputeSha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private byte[] CaptureAssetState(AssetObject asset)
        => m_serialization.Encode(
            writer => writer.WriteProperties(asset),
            m_serializationContext);

    private void RestoreAssetState(AssetObject asset, ReadOnlySpan<byte> state)
    {
        m_serialization.Decode(state, reader =>
        {
            reader.RestoreProperties(asset);
            return true;
        }, m_serializationContext);
    }

    private static void AddArtifactKey(HashSet<string> reachable, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            reachable.Add(value.ToUpperInvariant());
    }

    private bool IsInternalGeneratedPath(string relativePath)
        => AssetSourcePolicy.IsGeneratedPath(relativePath);

    private static void WriteAtomic(string targetPath, byte[] bytes)
        => AssetFileTransaction.WriteAtomic(targetPath, bytes);

    private static FileSnapshot CaptureFile(string path)
        => IOFile.Exists(path)
            ? new FileSnapshot(true, IOFile.ReadAllBytes(path))
            : new FileSnapshot(false, []);

    private static void RestoreFile(string path, FileSnapshot snapshot)
    {
        if (snapshot.existed)
        {
            WriteAtomic(path, snapshot.bytes);
            return;
        }

        DeleteIfExists(path);
    }

    private static void MoveGeneratedFile(string sourcePath, string targetPath)
    {
        if (!IOFile.Exists(sourcePath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (IOFile.Exists(targetPath))
            throw new IOException($"Generated metadata target '{targetPath}' already exists.");
        IOFile.Move(sourcePath, targetPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (IOFile.Exists(path))
            IOFile.Delete(path);
    }

    private static AssetDependencyData ToData(AssetDependency dependency) => new()
    {
        persistentId = dependency.persistentId,
        stableTypeId = dependency.type.stableId,
        lastKnownPath = dependency.lastKnownPath
    };

    private AssetImportDependencyData CreateImportDependencyDataLocked(
        string ownerPath,
        AssetImportDependency dependency)
    {
        if (dependency.kind == AssetImportDependencyKind.Source)
            ValidateSourceReferenceLocked(ownerPath, NormalizeRelativePath(dependency.key));
        var result = new AssetImportDependencyData
        {
            kind = (int)dependency.kind,
            key = dependency.key,
            fingerprint = dependency.fingerprint
        };
        result.fingerprint = ComputeImportDependencyFingerprintLocked(ref result, out _);
        return result;
    }

    private void ValidateSourceReferenceLocked(string ownerPath, string dependencyPath)
    {
        AssetPath owner = AssetPath.Parse(ownerPath);
        AssetPath dependency = AssetPath.Parse(dependencyPath);
        if (owner.source == dependency.source || owner.source == AssetSourceId.project)
            return;
        if (dependency.source == AssetSourceId.project)
        {
            throw new InvalidOperationException(
                $"Plugin source '{owner.source}' cannot reference project asset '{dependency.localPath}'.");
        }
        if (!m_mounts.TryGetValue(owner.source, out AssetSourceMount? ownerMount))
            throw new InvalidOperationException($"Asset source '{owner.source}' is not mounted.");
        if (!m_mounts.ContainsKey(dependency.source))
            throw new InvalidOperationException($"Asset source dependency '{dependency.source}' is not mounted.");
        if (!ownerMount.dependencySourceIds.Contains(dependency.source))
        {
            throw new InvalidOperationException(
                $"Plugin source '{owner.source}' did not declare dependency '{dependency.source}'.");
        }
    }

    private string ComputeImportDependencyFingerprintLocked(
        ref AssetImportDependencyData dependency,
        out bool metadataChanged)
    {
        metadataChanged = false;
        switch ((AssetImportDependencyKind)dependency.kind)
        {
            case AssetImportDependencyKind.Source:
            {
                string sourcePath = GetSourcePath(NormalizeRelativePath(dependency.key));
                if (!AssetSourceFileStamp.TryCapture(sourcePath, out AssetSourceFileStamp sourceStamp))
                    return "MISSING";
                if (SourceStampMatches(dependency, sourceStamp))
                    return dependency.fingerprint;
                byte[] sourceBytes = ReadStableSourceBytes(sourcePath, out sourceStamp);
                ApplySourceStamp(ref dependency, sourceStamp);
                metadataChanged = true;
                return ComputeSha256Hex(sourceBytes);
            }
            case AssetImportDependencyKind.Artifact:
                return Guid.TryParse(dependency.key, out Guid id) &&
                       m_recordsById.TryGetValue(id, out AssetRecord? record)
                    ? record.meta.artifactKey
                    : "MISSING";
            case AssetImportDependencyKind.Custom:
                return dependency.fingerprint;
            default:
                return "UNKNOWN";
        }
    }

    private static AssetDependency[] GetDirectDependencies(AssetMeta meta)
        => meta.runtimeDependencies.Select(static value => new AssetDependency(
            value.persistentId,
            new TypeRef(value.stableTypeId),
            value.lastKnownPath)).ToArray();

    private static AssetDependency FindDescriptor(AssetMeta meta, Guid persistentId)
    {
        AssetDependencyData data = meta.runtimeDependencies.FirstOrDefault(value => value.persistentId == persistentId);
        return data.persistentId == Guid.Empty
            ? default
            : new AssetDependency(data.persistentId, new TypeRef(data.stableTypeId), data.lastKnownPath);
    }

    private sealed class AssetRecord
    {
        internal string relativePath = string.Empty;
        internal Guid persistentId;
        internal Guid stableTypeId;
        internal AssetMeta meta = new();
        internal byte[] payload = [];
        internal AssetObject? asset;
        internal bool? lastSweepReachability;
        internal long importerGeneration;
    }

    private sealed class MissingAsset : AssetObject;

    private sealed class AssetDependencySet(AssetObject[] assets)
    {
        internal AssetObject[] assets { get; } = assets;
    }

    private sealed class AssetLoadTransaction
    {
        internal List<AssetRecord> createdRecords { get; } = [];
    }

    private readonly record struct ImportBuild(
        AssetMeta meta,
        AssetObject asset,
        byte[] payload,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs,
        AssetDependency[] dependencies);

    private readonly record struct SweepCandidate(
        AssetRecord record,
        WeakReference<AssetObject> reference);

    private readonly record struct FileSnapshot(bool existed, byte[] bytes);
}
