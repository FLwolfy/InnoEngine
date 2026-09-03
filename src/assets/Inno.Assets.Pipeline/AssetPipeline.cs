using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Scripting.Api;
using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides the single application-level entry point for importing, loading, saving and
/// collecting assets.
/// </summary>
public sealed class AssetPipeline : IDisposable, IAssetLookup, IAssetReferenceResolver
{
    private readonly Lock m_lifecycleLock = new();
    private readonly AssetCatalogParticipant m_catalogParticipant;
    private readonly AssetPipelineDiagnosticPublisher m_diagnostics;
    private readonly Logger m_log;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;

    private AssetLoader? m_loader;
    private AssetFileSystem? m_fileSystem;
    private IDisposable? m_catalogParticipantRegistration;
    private int m_ownerThreadId;
    private long m_revision;
    private AssetCacheOptions m_cacheOptions;
    private long m_lastArtifactCollectionTimestamp;
    private AssetPipelineOptions m_options;
    private bool m_catalogActivationInProgress;
    private bool m_catalogRecoveryRequired;
    private AssetSourceMountTransaction? m_sourceMountCandidate;

    /// <summary>
    /// Gets whether asset services are initialized.
    /// </summary>
    public bool isInitialized { get; private set; }

    /// <summary>
    /// Gets the absolute source asset root.
    /// </summary>
    public string assetRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the absolute root containing rebuildable asset database data.
    /// </summary>
    public string libraryRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the absolute generated artifact root.
    /// </summary>
    public string artifactRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the monotonic identity of the current committed asset and source-mount state.
    /// </summary>
    [ScriptingApiIgnore]
    public long revision => Interlocked.Read(ref m_revision);

    /// <summary>
    /// Gets the active isolated source mount snapshot.
    /// </summary>
    [ScriptingApiIgnore]
    public IReadOnlyList<AssetSourceMount> sourceMounts { get; private set; } = [];

    /// <summary>
    /// Occurs after an asset database transaction has committed.
    /// </summary>
    public event Action<AssetChangeSet>? Changed;

    /// <summary>
    /// Occurs after a canonical loaded asset has been updated in place.
    /// </summary>
    public event Action<AssetObject>? AssetReloaded;

    /// <summary>
    /// Occurs after a complete isolated source-mount generation is atomically replaced.
    /// </summary>
    [ScriptingApiIgnore]
    public event Action? SourceMountsChanged;

    /// <summary>
    /// Creates one isolated authoring or deployed-runtime asset pipeline.
    /// </summary>
    /// <param name="modules">
    /// The module host whose candidate generations coordinate importer refreshes.
    /// </param>
    /// <param name="types">
    /// The type catalog used to discover and resolve asset extensions.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry used for metadata and canonical asset state.
    /// </param>
    /// <param name="identities">
    /// The identity allocator that owns every canonical asset in this pipeline.
    /// </param>
    /// <param name="logs">
    /// The explicitly owned router receiving asset pipeline diagnostics.
    /// </param>
    /// <param name="diagnostics">
    /// The diagnostic hub that owns source database and asset processing reports.
    /// </param>
    /// <param name="options">
    /// The asset source, artifact and watcher configuration.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when a required service is null.
    /// </exception>
    [ScriptingApiIgnore]
    public AssetPipeline(
        ModuleHost modules,
        TypeCatalog types,
        SerializationRegistry serialization,
        IdentityAllocator identities,
        DiagnosticHub diagnostics,
        LogRouter logs,
        AssetPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(logs);
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.libraryRoot))
            throw new ArgumentException("Library root is required.", nameof(options));

        m_types = types;
        m_serialization = serialization;
        m_diagnostics = new AssetPipelineDiagnosticPublisher(diagnostics);
        m_log = logs.CreateLogger<AssetPipeline>();
        m_catalogParticipant = new AssetCatalogParticipant(this);

        lock (m_lifecycleLock)
        {
            AssetSourceMount[] mounts = options.sourceMounts?.ToArray()
                ?? [new AssetSourceMount(AssetSourceId.project, options.assetRoot, isReadOnly: false)];
            AssetSourceMount projectMount = mounts.SingleOrDefault(static mount => mount.id == AssetSourceId.project)
                ?? throw new ArgumentException("A project asset source mount is required.", nameof(options));
            if (projectMount.isReadOnly && options.mode != AssetPipelineMode.RuntimeArtifacts)
                throw new ArgumentException("The project asset source mount must be writable.", nameof(options));
            assetRoot = projectMount.rootPath;
            libraryRoot = Path.GetFullPath(options.libraryRoot);
            DeleteCandidateCatalogRoots(libraryRoot);
            artifactRoot = Path.Combine(libraryRoot, "Artifacts");
            sourceMounts = mounts;
            AssetLoader loader = new(
                types,
                serialization,
                identities,
                diagnostics,
                logs,
                mounts,
                libraryRoot,
                options.sourcePolicy,
                runtimeArtifactsOnly: options.mode == AssetPipelineMode.RuntimeArtifacts);
            AssetFileSystem fileSystem = new(
                mounts,
                autoStart: false,
                options.fileWatcherFlushDelayMs,
                options.sourcePolicy,
                requireWritableProject: options.mode == AssetPipelineMode.Authoring);
            loader.AssetReloaded += OnAssetReloaded;
            m_loader = loader;
            m_fileSystem = fileSystem;
            m_ownerThreadId = Environment.CurrentManagedThreadId;
            m_revision = 0;
            m_cacheOptions = options.cacheOptions;
            m_options = options with { sourceMounts = mounts };
            m_lastArtifactCollectionTimestamp = 0;
            isInitialized = true;
            try
            {
                loader.Rescan();
                CollectArtifactsIfDue(loader, force: true);
                fileSystem.Refresh();
                if (options.enableFileSystemWatcher && options.mode == AssetPipelineMode.Authoring)
                    fileSystem.Start();
                m_catalogParticipantRegistration = modules.RegisterCatalogParticipant(
                    m_catalogParticipant);
            }
            catch
            {
                ShutdownLocked();
                throw;
            }
        }
    }

    /// <summary>
    /// Releases watchers, catalog participants, canonical objects, and rebuildable staging state.
    /// </summary>
    public void Dispose()
    {
        lock (m_lifecycleLock)
            ShutdownLocked();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Validates and atomically replaces the complete source-mount generation while preserving the active
    /// generation after any candidate failure.
    /// </summary>
    /// <param name="mounts">
    /// A writable project source followed by zero or more read-only sources.
    /// </param>
    [ScriptingApiIgnore]
    public void ReplaceSourceMounts(IReadOnlyList<AssetSourceMount> mounts)
    {
        using AssetSourceMountTransaction transaction = PrepareSourceMounts(mounts);
        transaction.Activate();
        transaction.Complete();
    }

    /// <summary>
    /// Builds and validates an isolated source-mount candidate without changing active AssetPipeline state.
    /// </summary>
    /// <param name="mounts">
    /// A writable project source followed by zero or more read-only sources.
    /// </param>
    /// <returns>
    /// A transaction that can be inspected, activated, completed, or rolled back.
    /// </returns>
    [ScriptingApiIgnore]
    public AssetSourceMountTransaction PrepareSourceMounts(IReadOnlyList<AssetSourceMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        EnsureOwnerThread();
        AssetLoader? candidateLoader = null;
        AssetFileSystem? candidateFileSystem = null;
        AssetCatalogCandidate? catalogCandidate = null;
        lock (m_lifecycleLock)
        {
            if (m_sourceMountCandidate is not null)
                throw new InvalidOperationException("Another source-mount candidate is already pending.");
            AssetSourceMount[] snapshot = mounts.ToArray();
            AssetSourceMount projectMount = snapshot.SingleOrDefault(static mount => mount.id == AssetSourceId.project)
                ?? throw new ArgumentException("A project asset source mount is required.", nameof(mounts));
            if (projectMount.isReadOnly)
                throw new ArgumentException("The project asset source mount must be writable.", nameof(mounts));
            if (snapshot.Select(static mount => mount.id).Distinct().Count() != snapshot.Length)
                throw new ArgumentException("Asset source mount IDs must be unique.", nameof(mounts));
            try
            {
                catalogCandidate = GetLoader().PrepareCatalogCandidate(snapshot, m_options.sourcePolicy);
                candidateLoader = catalogCandidate.loader;
                candidateFileSystem = new AssetFileSystem(
                    snapshot,
                    autoStart: false,
                    m_options.fileWatcherFlushDelayMs,
                    m_options.sourcePolicy);
                candidateLoader.Rescan();
                candidateFileSystem.Refresh();
            }
            catch
            {
                candidateFileSystem?.Dispose();
                candidateLoader?.Dispose();
                catalogCandidate?.Dispose();
                throw;
            }

            var transaction = new AssetSourceMountTransaction(
                this,
                snapshot,
                catalogCandidate,
                candidateFileSystem);
            m_sourceMountCandidate = transaction;
            return transaction;
        }
    }

    internal void ActivatePreparedSourceMounts(AssetSourceMountTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureOwnerThread();
        lock (m_lifecycleLock)
        {
            EnsurePendingSourceMountTransaction(transaction);
            if (transaction.isActivated)
                return;
            _ = transaction.candidateLoader.RefreshRegistries();
            transaction.candidateFileSystem.Refresh();
            transaction.previousLoader = m_loader;
            transaction.previousFileSystem = m_fileSystem;
            transaction.previousMounts = sourceMounts;
            transaction.previousOptions = m_options;
            transaction.candidateLoader.AssetReloaded += OnAssetReloaded;
            m_loader = transaction.candidateLoader;
            m_fileSystem = transaction.candidateFileSystem;
            sourceMounts = transaction.sourceMounts;
            AssetSourceMount project = transaction.sourceMounts.Single(
                static mount => mount.id == AssetSourceId.project);
            assetRoot = project.rootPath;
            m_options = m_options with { assetRoot = assetRoot, sourceMounts = transaction.sourceMounts };
            m_revision++;
            transaction.isActivated = true;
        }
    }

    internal void CompletePreparedSourceMounts(AssetSourceMountTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureOwnerThread();
        AssetLoader? previousLoader;
        AssetFileSystem? previousFileSystem;
        Action? changed;
        lock (m_lifecycleLock)
        {
            EnsurePendingSourceMountTransaction(transaction);
            if (!transaction.isActivated)
                throw new InvalidOperationException("A source-mount candidate must be activated before completion.");
            transaction.catalogCandidate.Commit();
            if (m_options.enableFileSystemWatcher)
                transaction.candidateFileSystem.Start();
            previousLoader = transaction.previousLoader;
            previousFileSystem = transaction.previousFileSystem;
            transaction.previousLoader = null;
            transaction.previousFileSystem = null;
            transaction.isFinished = true;
            m_sourceMountCandidate = null;
            changed = SourceMountsChanged;
        }

        previousFileSystem?.Dispose();
        if (previousLoader is not null)
        {
            previousLoader.AssetReloaded -= OnAssetReloaded;
            previousLoader.Dispose();
        }
        transaction.catalogCandidate.Dispose();
        InvokeObservers(changed);
    }

    internal void RollbackPreparedSourceMounts(AssetSourceMountTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.isFinished)
            return;
        EnsureOwnerThread();
        lock (m_lifecycleLock)
        {
            EnsurePendingSourceMountTransaction(transaction);
            if (transaction.isActivated)
            {
                transaction.candidateLoader.AssetReloaded -= OnAssetReloaded;
                m_loader = transaction.previousLoader;
                m_fileSystem = transaction.previousFileSystem;
                sourceMounts = transaction.previousMounts ?? [];
                m_options = transaction.previousOptions;
                AssetSourceMount? project = sourceMounts.SingleOrDefault(
                    static mount => mount.id == AssetSourceId.project);
                assetRoot = project?.rootPath ?? string.Empty;
                m_revision++;
            }
            transaction.isFinished = true;
            m_sourceMountCandidate = null;
        }

        transaction.candidateFileSystem.Dispose();
        transaction.candidateLoader.Dispose();
        transaction.catalogCandidate.Dispose();
    }

    private void DeleteCandidateCatalogRoots(string activeLibraryRoot)
    {
        string root = Path.Combine(activeLibraryRoot, "AssetDatabase", "Candidates");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private void EnsurePendingSourceMountTransaction(AssetSourceMountTransaction transaction)
    {
        if (!ReferenceEquals(m_sourceMountCandidate, transaction))
            throw new InvalidOperationException("The source-mount transaction is not the current candidate.");
    }

    /// <summary>
    /// Loads a canonical asset by isolated source path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <returns>
    /// The canonical asset instance.
    /// </returns>
    public TAsset Load<TAsset>(AssetPath path) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(path, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{path}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Loads a canonical asset using a runtime asset type selected by an authoring workflow.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="assetType">
    /// The required concrete or base asset type.
    /// </param>
    /// <returns>
    /// The canonical asset instance assignable to <paramref name="assetType"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assetType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="assetType"/> does not derive from <see cref="AssetObject"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no compatible canonical asset can be loaded.
    /// </exception>
    [ScriptingApiIgnore]
    public AssetObject Load(AssetPath path, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(AssetObject).IsAssignableFrom(assetType))
        {
            throw new ArgumentException(
                $"Asset type '{assetType.FullName}' must derive from '{typeof(AssetObject).FullName}'.",
                nameof(assetType));
        }
        return GetLoader().Load(path, assetType) ?? throw new InvalidOperationException(
            $"Asset '{path}' cannot be loaded as '{assetType.FullName}'.");
    }

    /// <summary>
    /// Loads a canonical asset by persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <returns>
    /// The canonical asset instance.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no compatible asset can be loaded.
    /// </exception>
    public TAsset Load<TAsset>(Guid persistentId) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(persistentId, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Tries to load a canonical asset by isolated source path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="asset">
    /// The canonical asset when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible asset was loaded.
    /// </returns>
    public bool TryLoad<TAsset>(AssetPath path, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(path, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>
    /// Tries to load a canonical asset by persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="asset">
    /// The canonical asset when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible asset was loaded.
    /// </returns>
    public bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(persistentId, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>
    /// Asynchronously loads a canonical asset by isolated source path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the current caller's wait.
    /// </param>
    /// <returns>
    /// The canonical asset instance.
    /// </returns>
    public async ValueTask<TAsset> LoadAsync<TAsset>(
        AssetPath path,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        AssetLoader loader = GetLoader();
        AssetObject? asset = await loader
            .LoadAsync(path, typeof(TAsset), cancellationToken)
            .ConfigureAwait(false);
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{path}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Asynchronously loads a canonical asset by persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset type.
    /// </typeparam>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the current caller's wait.
    /// </param>
    /// <returns>
    /// The canonical asset instance.
    /// </returns>
    public async ValueTask<TAsset> LoadAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        AssetLoader loader = GetLoader();
        AssetObject? asset = await loader
            .LoadAsync(persistentId, typeof(TAsset), cancellationToken)
            .ConfigureAwait(false);
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Imports one source asset from an isolated source mount.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer handled the source.
    /// </returns>
    public bool Import(AssetPath path)
    {
        EnsureOwnerThread();
        AssetLoader loader = GetLoader();
        bool imported = loader.Import(path);
        if (imported)
        {
            GetFileSystem().Refresh();
            _ = loader.TryGetPersistentId(path, out Guid persistentId);
            PublishMutation(new AssetChange(AssetChangeKind.Modified, persistentId, path));
        }
        return imported;
    }

    /// <summary>
    /// Saves an asset to its current source path.
    /// </summary>
    /// <param name="asset">
    /// The asset to save.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer exported the asset.
    /// </returns>
    public bool Save(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Save(asset.assetPath, asset);
    }

    /// <summary>
    /// Saves an asset to a writable isolated source path.
    /// </summary>
    /// <param name="path">
    /// Writable isolated source path.
    /// </param>
    /// <param name="asset">
    /// Asset to save.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an importer exported the asset.
    /// </returns>
    public bool Save(AssetPath path, AssetObject asset)
    {
        EnsureOwnerThread();
        _ = NormalizeMutationPath(path, nameof(path));
        ArgumentNullException.ThrowIfNull(asset);
        AssetLoader loader = GetLoader();
        bool existed = loader.TryGetPersistentId(path, out _);
        bool saved = loader.Save(path, asset);
        if (saved)
        {
            GetFileSystem().Refresh();
            _ = loader.TryGetPersistentId(path, out Guid persistentId);
            PublishMutation(new AssetChange(
                existed ? AssetChangeKind.Modified : AssetChangeKind.Added,
                persistentId,
                path));
        }
        return saved;
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
            return GetLoader().ResolveReference(
                persistentId,
                stableTypeId,
                lastKnownPath,
                expectedType);
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
    /// Moves a source asset while preserving its persistent identity and generated metadata.
    /// </summary>
    /// <param name="source">
    /// Existing isolated source path.
    /// </param>
    /// <param name="target">
    /// New isolated source path.
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the source does not exist.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the target source or metadata already exists.
    /// </exception>
    public void Move(AssetPath source, AssetPath target)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(source, nameof(source));
        string targetPath = NormalizeMutationPath(target, nameof(target));
        if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            return;

        AssetFileSystem fileSystem = GetFileSystem();
        AssetLoader loader = GetLoader();
        if (fileSystem.isWatching)
        {
            IReadOnlyList<AssetChangedEvent> pending = fileSystem.WaitForIdle(out bool requiresFullRescan);
            if (pending.Count > 0 || requiresFullRescan)
                ApplySourceChanges(pending, requiresFullRescan);
        }

        string absoluteSource = Path.Combine(assetRoot, sourcePath);
        string absoluteTarget = Path.Combine(assetRoot, targetPath);
        bool isDirectory = Directory.Exists(absoluteSource);
        if (!isDirectory && !System.IO.File.Exists(absoluteSource))
            throw new FileNotFoundException($"Asset source '{sourcePath}' does not exist.", absoluteSource);
        if (Directory.Exists(absoluteTarget) || System.IO.File.Exists(absoluteTarget))
            throw new IOException($"Asset move target '{targetPath}' already exists.");
        if (System.IO.File.Exists(absoluteTarget + ".imeta"))
            throw new IOException($"Asset move target metadata '{targetPath}.imeta' already exists.");

        bool restartWatcher = fileSystem.isWatching;
        fileSystem.Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteTarget)!);
        var change = new AssetChangedEvent(targetPath, WatcherChangeTypes.Renamed, sourcePath);
        Dictionary<string, Guid> previousIds = CapturePreviousIds(loader, [change]);
        try
        {
            MovePhysicalSource(absoluteSource, absoluteTarget, isDirectory);
            loader.ApplySourceChanges([change]);
            fileSystem.Refresh();
            AssetChange[] committed = CreateCommittedChanges(loader, [change], previousIds, requiresFullRescan: false);
            InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref m_revision), committed));
        }
        catch
        {
            TryRollbackPhysicalMove(absoluteSource, absoluteTarget, isDirectory);
            TryRollbackMetadataMove(absoluteSource + ".imeta", absoluteTarget + ".imeta");
            loader.Rescan();
            fileSystem.Refresh();
            throw;
        }
        finally
        {
            if (restartWatcher)
                fileSystem.Start();
        }
    }

    /// <summary>
    /// Deletes a source asset and its metadata while retaining a Library tombstone for existing references.
    /// </summary>
    /// <param name="path">
    /// Existing isolated file or directory path.
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the source does not exist.
    /// </exception>
    public void Delete(AssetPath path)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(path, nameof(path));
        AssetFileSystem fileSystem = GetFileSystem();
        AssetLoader loader = GetLoader();
        DrainPendingChanges(fileSystem);

        string absoluteSource = Path.Combine(assetRoot, sourcePath);
        bool isDirectory = Directory.Exists(absoluteSource);
        if (!isDirectory && !System.IO.File.Exists(absoluteSource))
            throw new FileNotFoundException($"Asset source '{sourcePath}' does not exist.", absoluteSource);

        AssetChangedEvent[] changes = CreateDeletionEvents(fileSystem, sourcePath);
        Dictionary<string, Guid> previousIds = CapturePreviousIds(loader, changes);
        string transactionRoot = Path.Combine(
            libraryRoot,
            "AssetDatabase",
            "Transactions",
            Guid.NewGuid().ToString("N"));
        string stagedSource = Path.Combine(transactionRoot, "source");
        string sourceMeta = absoluteSource + ".imeta";
        string stagedMeta = Path.Combine(transactionRoot, "source.imeta");
        bool restartWatcher = fileSystem.isWatching;
        fileSystem.Stop();
        Directory.CreateDirectory(transactionRoot);
        AssetChange[] committed;
        try
        {
            MovePhysicalSource(absoluteSource, stagedSource, isDirectory);
            if (System.IO.File.Exists(sourceMeta))
                System.IO.File.Move(sourceMeta, stagedMeta);
            loader.ApplySourceChanges(changes);
            fileSystem.Refresh();
            committed = CreateCommittedChanges(loader, changes, previousIds, requiresFullRescan: false);
        }
        catch
        {
            RestoreStagedDeletion(absoluteSource, stagedSource, isDirectory);
            RestoreStagedMetadata(sourceMeta, stagedMeta);
            loader.Rescan();
            fileSystem.Refresh();
            DeleteTransactionDirectorySafely(transactionRoot);
            throw;
        }
        finally
        {
            if (restartWatcher)
                fileSystem.Start();
        }
        DeleteTransactionDirectorySafely(transactionRoot);
        InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref m_revision), committed));
        CollectArtifactsIfDue(loader, force: true);
    }

    /// <summary>
    /// Creates a tracked source directory and its persistent metadata.
    /// </summary>
    /// <param name="path">
    /// New isolated directory path.
    /// </param>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when the parent directory does not exist.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the target already exists.
    /// </exception>
    public void CreateDirectory(AssetPath path)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(path, nameof(path));
        AssetFileSystem fileSystem = GetFileSystem();
        AssetLoader loader = GetLoader();
        DrainPendingChanges(fileSystem);

        string absolutePath = Path.Combine(assetRoot, sourcePath);
        string parentPath = Path.GetDirectoryName(absolutePath)!;
        if (!Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"Asset directory parent '{Path.GetDirectoryName(sourcePath)}' does not exist.");
        }
        if (Directory.Exists(absolutePath) || System.IO.File.Exists(absolutePath))
            throw new IOException($"Asset directory target '{sourcePath}' already exists.");
        if (System.IO.File.Exists(absolutePath + ".imeta"))
            throw new IOException($"Asset directory target metadata '{sourcePath}.imeta' already exists.");

        bool restartWatcher = fileSystem.isWatching;
        fileSystem.Stop();
        var change = new AssetChangedEvent(sourcePath, WatcherChangeTypes.Created);
        try
        {
            Directory.CreateDirectory(absolutePath);
            loader.Rescan();
            fileSystem.Refresh();
            AssetChange[] committed = CreateCommittedChanges(
                loader,
                [change],
                new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase),
                requiresFullRescan: false);
            InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref m_revision), committed));
        }
        catch
        {
            if (Directory.Exists(absolutePath))
                Directory.Delete(absolutePath, recursive: true);
            if (System.IO.File.Exists(absolutePath + ".imeta"))
                System.IO.File.Delete(absolutePath + ".imeta");
            loader.Rescan();
            fileSystem.Refresh();
            throw;
        }
        finally
        {
            if (restartWatcher)
                fileSystem.Start();
        }
    }

    /// <summary>
    /// Imports an authoring-only sample directory into the writable Assets root.
    /// </summary>
    /// <param name="source">
    /// An indexed sample directory whose final segment starts with <c>~</c>.
    /// </param>
    /// <returns>
    /// The new writable project path with the sample prefix removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> is not an indexed sample directory.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the destination exists, the source contains symbolic links, or the source changes
    /// while its stable import snapshot is being copied.
    /// </exception>
    public AssetPath ImportSample(AssetPath source)
    {
        EnsureOwnerThread();
        AssetFileSystem fileSystem = GetFileSystem();
        AssetLoader loader = GetLoader();
        DrainPendingChanges(fileSystem);
        if (!fileSystem.TryGetEntry(source, out AssetFileEntry entry) ||
            !entry.isDirectory ||
            !entry.isSample)
        {
            throw new ArgumentException(
                $"Asset source '{source}' is not an authoring-only sample directory.",
                nameof(source));
        }

        AssetSourceMount sourceMount = sourceMounts.Single(mount => mount.id == source.source);
        string absoluteSource = sourceMount.Resolve(source.localPath);
        AssetPath target = AssetPath.Project(AssetSample.GetImportName(source));
        string absoluteTarget = Path.Combine(assetRoot, target.localPath);
        string targetMeta = absoluteTarget + ".imeta";
        if (Directory.Exists(absoluteTarget) || System.IO.File.Exists(absoluteTarget) || System.IO.File.Exists(targetMeta))
            throw new IOException($"Sample import target '{target}' already exists.");

        string transactionRoot = Path.Combine(
            libraryRoot,
            "AssetDatabase",
            "Transactions",
            Guid.NewGuid().ToString("N"));
        string stagedSource = Path.Combine(transactionRoot, "sample");
        string stagedMeta = stagedSource + ".imeta";
        AssetSourcePolicy sourcePolicy = m_options.sourcePolicy ?? AssetSourcePolicy.defaultPolicy;
        bool restartWatcher = fileSystem.isWatching;
        bool sourceCommitted = false;
        bool metaCommitted = false;
        fileSystem.Stop();
        Directory.CreateDirectory(transactionRoot);
        try
        {
            List<string> copiedEntries = AssetSampleSnapshot.Capture(
                absoluteSource,
                stagedSource,
                target.localPath,
                sourcePolicy);

            Directory.Move(stagedSource, absoluteTarget);
            sourceCommitted = true;
            if (System.IO.File.Exists(stagedMeta))
            {
                System.IO.File.Move(stagedMeta, targetMeta);
                metaCommitted = true;
            }

            var changes = new List<AssetChangedEvent>(copiedEntries.Count + 1)
            {
                new(target.localPath, WatcherChangeTypes.Created)
            };
            changes.AddRange(copiedEntries.Select(static path =>
                new AssetChangedEvent(path, WatcherChangeTypes.Created)));
            loader.Rescan();
            fileSystem.Refresh();
            AssetChange[] committed = CreateCommittedChanges(
                loader,
                changes,
                new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase),
                requiresFullRescan: false);
            InvokeObservers(
                Changed,
                new AssetChangeSet(Interlocked.Increment(ref m_revision), committed));
            CollectArtifactsIfDue(loader, force: true);
            return target;
        }
        catch
        {
            if (metaCommitted && System.IO.File.Exists(targetMeta))
                System.IO.File.Delete(targetMeta);
            if (sourceCommitted && Directory.Exists(absoluteTarget))
                Directory.Delete(absoluteTarget, recursive: true);
            loader.Rescan();
            fileSystem.Refresh();
            throw;
        }
        finally
        {
            DeleteTransactionDirectorySafely(transactionRoot);
            if (restartWatcher)
                fileSystem.Start();
        }
    }

    /// <summary>
    /// Reconciles source files, generated files and the persistent catalog.
    /// </summary>
    public void Rescan()
    {
        EnsureOwnerThread();
        PruneRetiredObservers();
        GetLoader().Rescan();
        GetFileSystem().Refresh();
    }

    /// <summary>
    /// Applies queued source and build changes on the initialization thread.
    /// </summary>
    public void Update()
    {
        EnsureOwnerThread();
        PruneRetiredObservers();
        AssetFileSystem fileSystem = GetFileSystem();
        IReadOnlyList<AssetChangedEvent> changes = fileSystem.PollChanges(out bool requiresFullRescan);
        bool registriesChanged = GetLoader().RefreshRegistries();
        if (changes.Count == 0 && !requiresFullRescan && !registriesChanged)
        {
            CollectArtifactsIfDue(GetLoader(), force: false);
            return;
        }
        ApplySourceChanges(changes, requiresFullRescan || registriesChanged);
        CollectArtifactsIfDue(GetLoader(), force: false);
    }

    /// <summary>
    /// Collects assets that have no external managed references.
    /// </summary>
    /// <returns>
    /// The number of released canonical assets.
    /// </returns>
    public int UnloadUnusedAssets() => GetLoader().UnloadUnusedAssets();

    /// <summary>
    /// Tries to resolve an asset type without loading the asset.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="assetType">
    /// The resolved concrete asset type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type can be resolved.
    /// </returns>
    public bool TryGetAssetType(AssetPath path, out Type? assetType)
        => GetLoader().TryGetAssetType(path, out assetType);

    /// <summary>
    /// Tries to resolve a persistent identity without loading the asset.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="persistentId">
    /// The resolved persistent identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when catalog metadata exists.
    /// </returns>
    public bool TryGetPersistentId(AssetPath path, out Guid persistentId)
        => GetLoader().TryGetPersistentId(path, out persistentId);

    /// <summary>
    /// Tries to get a catalog snapshot by source-relative path.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="info">
    /// The catalog snapshot when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the path is cataloged.
    /// </returns>
    public bool TryGetInfo(AssetPath path, out AssetInfo? info)
        => GetLoader().TryGetInfo(path, out info);

    /// <summary>
    /// Tries to get a catalog snapshot by persistent identity.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="info">
    /// The catalog snapshot when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identity is cataloged.
    /// </returns>
    public bool TryGetInfo(Guid persistentId, out AssetInfo? info)
        => GetLoader().TryGetInfo(persistentId, out info);

    /// <summary>
    /// Tries to resolve a named artifact output.
    /// </summary>
    /// <param name="persistentId">
    /// The artifact owner identity.
    /// </param>
    /// <param name="outputName">
    /// The named output.
    /// </param>
    /// <param name="artifact">
    /// The output information when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the output exists.
    /// </returns>
    public bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
        => GetLoader().TryGetArtifact(persistentId, outputName, out artifact);

    /// <summary>
    /// Runs an aggregate asset build using the processor registered for a definition.
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
        EnsureOwnerThread();
        return GetLoader().BuildAsync(definition, inputs, cancellationToken);
    }

    /// <summary>
    /// Exports the current source-free runtime catalog and its exact artifact closure.
    /// </summary>
    /// <param name="destinationContentRoot">
    /// Empty destination for the deployed asset database.
    /// </param>
    /// <returns>
    /// Counts and source identities for the deployed runtime snapshot.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a runtime-scoped asset has no complete artifact or depends on an authoring-only asset.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the destination is not empty or cannot be written.
    /// </exception>
    [ScriptingApiIgnore]
    public AssetRuntimeContentInfo ExportRuntimeArtifacts(string destinationContentRoot)
    {
        EnsureOwnerThread();
        WaitForIdle();
        using SerializationGeneration serialization = m_serialization.CaptureGeneration();
        return GetLoader().ExportRuntimeArtifacts(
            destinationContentRoot,
            serialization,
            CancellationToken.None);
    }

    /// <summary>
    /// Exports a runtime-only artifact snapshot on a worker without loading artifact files into memory.
    /// </summary>
    /// <param name="destinationContentRoot">
    /// The empty destination that receives the runtime asset database.
    /// </param>
    /// <param name="cancellationToken">
    /// The token checked while individual artifact files are copied.
    /// </param>
    /// <returns>
    /// A task that completes with counts and source identities for the captured runtime snapshot.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when runtime artifacts are incomplete or the asset service is not available.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when cancellation is requested before export completes.
    /// </exception>
    [ScriptingApiIgnore]
    public Task<AssetRuntimeContentInfo> ExportRuntimeArtifactsAsync(
        string destinationContentRoot,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnerThread();
        WaitForIdle();
        AssetLoader loader = GetLoader();
        SerializationGeneration serialization = m_serialization.CaptureGeneration();
        return Task.Run(
            () =>
            {
                using (serialization)
                {
                    return loader.ExportRuntimeArtifacts(
                        destinationContentRoot,
                        serialization,
                        cancellationToken);
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Gets isolated source paths for all canonical loaded assets.
    /// </summary>
    /// <returns>
    /// A stable isolated path snapshot.
    /// </returns>
    public IReadOnlyList<AssetPath> GetLoadedPaths()
        => GetLoader().GetLoadedPaths();

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
    /// The dependency descriptors.
    /// </returns>
    public IReadOnlyList<AssetDependency> GetDependencies(
        AssetObject asset,
        bool recursive = false)
        => GetLoader().GetDependencies(asset, recursive);

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
    public IReadOnlyList<AssetPath> GetImportDependencies(
        AssetObject asset,
        bool recursive = false)
        => GetLoader().GetImportDependencies(asset, recursive);

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
        => GetLoader().GetReferenceInfo(asset);

    /// <summary>
    /// Gets indexed source entries.
    /// </summary>
    /// <param name="includeDirectories">
    /// Whether directories should be included.
    /// </param>
    /// <returns>
    /// The stable source entry snapshot.
    /// </returns>
    public IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
        => GetFileSystem().GetEntries(includeDirectories);

    /// <summary>
    /// Gets immediate indexed children of a source directory.
    /// </summary>
    /// <param name="parent">
    /// The isolated parent path.
    /// </param>
    /// <returns>
    /// The immediate child entry snapshot.
    /// </returns>
    public IReadOnlyList<AssetFileEntry> GetFileSystemChildren(AssetPath parent)
        => GetFileSystem().GetChildren(parent);

    /// <summary>
    /// Tries to resolve an indexed source entry.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="entry">
    /// The resolved source entry.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the entry exists and is not generated metadata.
    /// </returns>
    public bool TryGetFileSystemEntry(AssetPath path, out AssetFileEntry entry)
    {
        return GetFileSystem().TryGetEntry(path, out entry);
    }

    /// <summary>
    /// Waits until queued source watcher changes have been processed.
    /// </summary>
    public void WaitForIdle()
    {
        EnsureOwnerThread();
        AssetFileSystem fileSystem = GetFileSystem();
        IReadOnlyList<AssetChangedEvent> changes = fileSystem.WaitForIdle(out bool requiresFullRescan);
        if (changes.Count > 0 || requiresFullRescan)
            ApplySourceChanges(changes, requiresFullRescan);
        GetLoader().WaitForIdle();
        CollectArtifactsIfDue(GetLoader(), force: true);
    }

    private void ApplySourceChanges(
        IReadOnlyList<AssetChangedEvent> changes,
        bool requiresFullRescan)
    {
        AssetLoader? loader = m_loader;
        if (loader is null)
            return;
        Dictionary<string, Guid> previousIds = CapturePreviousIds(loader, changes);
        try
        {
            if (requiresFullRescan)
                loader.Rescan();
            else
                loader.ApplySourceChanges(changes);
            m_diagnostics.ResolveSourceDatabase();
        }
        catch (Exception exception)
        {
            try
            {
                loader.Rescan();
                m_diagnostics.ResolveSourceDatabase();
                m_log.Write(
                    LogLevel.Warn,
                    "Asset source refresh failed and was recovered by a full rescan: {0}",
                    [exception]);
            }
            catch (Exception recoveryException)
            {
                m_log.Write(
                    LogLevel.Error,
                    "Asset source refresh and recovery rescan both failed. Refresh: {0} Recovery: {1}",
                    [exception, recoveryException]);
                m_diagnostics.PublishSourceDatabaseFailure(
                    exception,
                    recoveryException);
            }
        }
        AssetChange[] committed = CreateCommittedChanges(loader, changes, previousIds, requiresFullRescan);
        InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref m_revision), committed));
    }

    private void DrainPendingChanges(AssetFileSystem fileSystem)
    {
        if (!fileSystem.isWatching)
            return;
        IReadOnlyList<AssetChangedEvent> pending = fileSystem.WaitForIdle(out bool requiresFullRescan);
        if (pending.Count > 0 || requiresFullRescan)
            ApplySourceChanges(pending, requiresFullRescan);
    }

    private AssetChangedEvent[] CreateDeletionEvents(
        AssetFileSystem fileSystem,
        string sourcePath)
    {
        string prefix = sourcePath + "/";
        return fileSystem.GetEntries()
            .Where(entry =>
                string.Equals(entry.assetPath.ToString(), sourcePath, StringComparison.OrdinalIgnoreCase) ||
                entry.assetPath.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.assetPath.ToString().Length)
            .Select(static entry => new AssetChangedEvent(entry.assetPath.ToString(), WatcherChangeTypes.Deleted))
            .DefaultIfEmpty(new AssetChangedEvent(sourcePath, WatcherChangeTypes.Deleted))
            .ToArray();
    }

    private void OnAssetReloaded(AssetObject asset)
        => InvokeObservers(AssetReloaded, asset);

    private void InvokeObservers<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
            return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<T>)handler)(value);
            }
            catch
            {
                // Observer failures cannot roll back committed manager state.
            }
        }
    }

    private void InvokeObservers(Action? handlers)
    {
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
                // Observer failures cannot roll back committed manager state.
            }
        }
    }

    private void PruneRetiredObservers()
    {
        RemoveRetiredObservers(Changed, observer => Changed -= observer);
        RemoveRetiredObservers(AssetReloaded, observer => AssetReloaded -= observer);
    }

    private void RemoveRetiredObservers<T>(
        Action<T>? handlers,
        Action<Action<T>> remove)
    {
        if (handlers is null)
            return;
        foreach (Delegate observer in handlers.GetInvocationList())
        {
            if (!IsRetiredCollectibleObserver(observer))
                continue;
            remove((Action<T>)observer);
        }
    }

    private bool IsRetiredCollectibleObserver(Delegate observer)
    {
        Type? declaringType = observer.Method.DeclaringType;
        Type? targetType = observer.Target?.GetType();
        return IsRetiredCollectibleType(declaringType) ||
               IsRetiredCollectibleType(targetType);
    }

    private bool IsRetiredCollectibleType(Type? type)
    {
        if (type is null ||
            AssemblyLoadContext.GetLoadContext(type.Assembly) is not { IsCollectible: true })
        {
            return false;
        }
        return !m_types.TryGetTypeRef(type, out _);
    }

    private AssetLoader GetLoader()
    {
        AssetLoader loader = isInitialized && m_loader is not null
            ? m_loader
            : throw new InvalidOperationException("AssetPipeline is not initialized.");
        RecoverCatalogIfRequired(loader);
        return loader;
    }

    private void RecoverCatalogIfRequired(AssetLoader loader)
    {
        if (!m_catalogRecoveryRequired || m_catalogActivationInProgress)
            return;
        EnsureOwnerThread();
        m_catalogActivationInProgress = true;
        try
        {
            loader.Rescan();
            m_fileSystem?.Refresh();
            m_catalogRecoveryRequired = false;
        }
        finally
        {
            m_catalogActivationInProgress = false;
        }
    }

    private AssetFileSystem GetFileSystem()
        => isInitialized && m_fileSystem is not null
            ? m_fileSystem
            : throw new InvalidOperationException("AssetPipeline is not initialized.");

    private Dictionary<string, Guid> CapturePreviousIds(
        AssetLoader loader,
        IReadOnlyList<AssetChangedEvent> changes)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < changes.Count; i++)
        {
            AssetChangedEvent change = changes[i];
            string path = string.IsNullOrWhiteSpace(change.oldRelativePath)
                ? change.relativePath
                : change.oldRelativePath;
            if (loader.TryGetPersistentId(AssetPath.Parse(path), out Guid id))
                result[path] = id;
        }
        return result;
    }

    private AssetChange[] CreateCommittedChanges(
        AssetLoader loader,
        IReadOnlyList<AssetChangedEvent> changes,
        IReadOnlyDictionary<string, Guid> previousIds,
        bool requiresFullRescan)
    {
        if (requiresFullRescan && changes.Count == 0)
            return [new AssetChange(AssetChangeKind.StatusChanged, Guid.Empty, AssetPath.Project(string.Empty))];

        var result = new List<AssetChange>(changes.Count);
        for (int i = 0; i < changes.Count; i++)
        {
            AssetChangedEvent change = changes[i];
            bool moved = change.changeType.HasFlag(WatcherChangeTypes.Renamed);
            bool deleted = change.changeType.HasFlag(WatcherChangeTypes.Deleted);
            Guid id = Guid.Empty;
            if (!loader.TryGetPersistentId(AssetPath.Parse(change.relativePath), out id))
            {
                string previousPath = moved ? change.oldRelativePath : change.relativePath;
                _ = previousIds.TryGetValue(previousPath, out id);
            }
            AssetChangeKind kind = moved
                ? AssetChangeKind.Moved
                : deleted
                    ? System.IO.File.Exists(Path.Combine(assetRoot, change.relativePath + ".imeta"))
                        ? AssetChangeKind.Missing
                        : AssetChangeKind.Removed
                    : change.changeType.HasFlag(WatcherChangeTypes.Created)
                        ? AssetChangeKind.Added
                        : AssetChangeKind.Modified;
            result.Add(new AssetChange(
                kind,
                id,
                AssetPath.Parse(change.relativePath),
                string.IsNullOrWhiteSpace(change.oldRelativePath)
                    ? null
                    : AssetPath.Parse(change.oldRelativePath)));
        }
        return result.ToArray();
    }

    private void PublishMutation(AssetChange change)
        => InvokeObservers(
            Changed,
            new AssetChangeSet(Interlocked.Increment(ref m_revision), [change]));

    private void EnsureOwnerThread()
    {
        if (!isInitialized)
            throw new InvalidOperationException("AssetPipeline is not initialized.");
        if (Environment.CurrentManagedThreadId != m_ownerThreadId)
        {
            throw new InvalidOperationException(
                "Asset database mutations must run on the thread that initialized AssetPipeline.");
        }
    }

    private string NormalizeMutationPath(AssetPath path, string parameterName)
    {
        if (!path.isValid || string.IsNullOrWhiteSpace(path.localPath))
            throw new ArgumentException("Asset source path is required.", parameterName);
        if (path.source != AssetSourceId.project)
            throw new InvalidOperationException($"Asset source '{path.source}' is read-only.");
        return path.localPath;
    }

    private void MovePhysicalSource(string sourcePath, string targetPath, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(sourcePath, targetPath);
        else
            System.IO.File.Move(sourcePath, targetPath);
    }

    private void TryRollbackPhysicalMove(string sourcePath, string targetPath, bool isDirectory)
    {
        try
        {
            bool targetExists = isDirectory ? Directory.Exists(targetPath) : System.IO.File.Exists(targetPath);
            bool sourceExists = isDirectory ? Directory.Exists(sourcePath) : System.IO.File.Exists(sourcePath);
            if (targetExists && !sourceExists)
                MovePhysicalSource(targetPath, sourcePath, isDirectory);
        }
        catch
        {
            // The following catalog rescan reports any remaining physical conflict.
        }
    }

    private void TryRollbackMetadataMove(string sourcePath, string targetPath)
    {
        try
        {
            if (System.IO.File.Exists(targetPath) && !System.IO.File.Exists(sourcePath))
                System.IO.File.Move(targetPath, sourcePath);
        }
        catch
        {
            // The following catalog rescan reports any remaining metadata conflict.
        }
    }

    private void RestoreStagedDeletion(
        string sourcePath,
        string stagedSource,
        bool isDirectory)
    {
        if (isDirectory ? Directory.Exists(stagedSource) : System.IO.File.Exists(stagedSource))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            MovePhysicalSource(stagedSource, sourcePath, isDirectory);
        }
    }

    private void RestoreStagedMetadata(string metaPath, string stagedMetaPath)
    {
        if (!System.IO.File.Exists(stagedMetaPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        System.IO.File.Move(stagedMetaPath, metaPath);
    }

    private void DeleteTransactionDirectorySafely(string transactionRoot)
    {
        try
        {
            if (Directory.Exists(transactionRoot))
                Directory.Delete(transactionRoot, recursive: true);
        }
        catch (IOException)
        {
            // Rebuildable transaction debris is retried by later Library maintenance.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only transaction debris must not roll back an already committed deletion.
        }
    }

    private void CollectArtifactsIfDue(AssetLoader loader, bool force)
    {
        if (m_options.mode == AssetPipelineMode.RuntimeArtifacts)
            return;
        long now = Environment.TickCount64;
        if (!force && now - m_lastArtifactCollectionTimestamp < 60_000)
            return;
        m_lastArtifactCollectionTimestamp = now;
        _ = loader.CollectArtifacts(
            m_cacheOptions.garbageCollectionGracePeriod,
            m_cacheOptions.maximumSizeBytes);
    }

    private void ShutdownLocked()
    {
        if (m_sourceMountCandidate is not null)
        {
            AssetSourceMountTransaction candidate = m_sourceMountCandidate;
            if (candidate.isActivated)
            {
                candidate.candidateLoader.AssetReloaded -= OnAssetReloaded;
                m_loader = candidate.previousLoader;
                m_fileSystem = candidate.previousFileSystem;
            }
            candidate.candidateFileSystem.Dispose();
            candidate.candidateLoader.Dispose();
            candidate.isFinished = true;
            m_sourceMountCandidate = null;
        }
        m_catalogParticipantRegistration?.Dispose();
        m_catalogParticipantRegistration = null;
        m_diagnostics.ResolveSourceDatabase();
        if (m_fileSystem is not null)
        {
            m_fileSystem.Dispose();
        }
        if (m_loader is not null)
        {
            m_loader.AssetReloaded -= OnAssetReloaded;
            m_loader.Dispose();
        }
        m_fileSystem = null;
        m_loader = null;
        assetRoot = string.Empty;
        libraryRoot = string.Empty;
        artifactRoot = string.Empty;
        sourceMounts = [];
        m_ownerThreadId = 0;
        m_revision = 0;
        m_cacheOptions = default;
        m_options = default;
        m_catalogActivationInProgress = false;
        m_catalogRecoveryRequired = false;
        m_lastArtifactCollectionTimestamp = 0;
        isInitialized = false;
        Changed = null;
        AssetReloaded = null;
        SourceMountsChanged = null;
    }

    private sealed class AssetCatalogParticipant(AssetPipeline owner) : IAssemblyCatalogParticipant
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        /// <param name="catalog">
        /// The candidate asset catalog prepared for activation.
        /// </param>
        /// <returns>
        /// The validated iassembly catalog transaction that represents the completed operation.
        /// </returns>
        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            AssetImportHealthSnapshot existingFailures =
                owner.isInitialized && owner.m_loader is not null
                    ? owner.m_loader.CaptureWritableImportHealth()
                    : AssetImportHealthSnapshot.empty;
            return new AssetCatalogTransaction(owner, existingFailures);
        }
    }

    private sealed class AssetCatalogTransaction(
        AssetPipeline owner,
        AssetImportHealthSnapshot existingFailures) : IAssemblyCatalogTransaction
    {
        private readonly AssetImportHealthSnapshot m_existingFailures = existingFailures;
        private bool m_activated;
        private bool m_finished;

        /// <summary>
        /// Gets the candidate activation context shared with participating registries.
        /// </summary>
        public object? context => null;

        /// <summary>
        /// Makes the prepared value active at the owning subsystem's safety point.
        /// </summary>
        public void Activate()
        {
            EnsureNotFinished();
            if (!owner.isInitialized || owner.m_loader is null)
            {
                m_activated = true;
                return;
            }

            owner.EnsureOwnerThread();
            owner.m_catalogActivationInProgress = true;
            try
            {
                owner.m_loader.Rescan();
                IReadOnlyList<AssetImportFailure> candidateFailures =
                    owner.m_loader.FindIntroducedImportFailures(m_existingFailures);
                if (candidateFailures.Count != 0)
                {
                    string details = string.Join(
                        "; ",
                        candidateFailures.Select(static failure =>
                            $"{failure.assetPath}: {failure.diagnostics}"));
                    throw new InvalidDataException(
                        "The candidate assembly catalog introduced or changed writable Asset import failures: "
                        + details);
                }
                owner.m_fileSystem?.Refresh();
                owner.m_catalogRecoveryRequired = false;
                m_activated = true;
            }
            catch
            {
                owner.m_catalogRecoveryRequired = true;
                throw;
            }
            finally
            {
                owner.m_catalogActivationInProgress = false;
            }
        }

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete()
        {
            EnsureNotFinished();
            if (!m_activated)
                throw new InvalidOperationException("Asset catalog transaction has not been activated.");
            m_finished = true;
        }

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void Rollback()
        {
            if (m_finished)
                return;
            if (m_activated && owner.isInitialized)
                owner.m_catalogRecoveryRequired = true;
            m_finished = true;
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Asset catalog transaction is already finished.");
        }
    }
}
