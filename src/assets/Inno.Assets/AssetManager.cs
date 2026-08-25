using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Core.Scripting;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Provides the single application-level entry point for importing, loading, saving and
/// collecting assets.
/// </summary>
public static class AssetManager
{
    private static readonly Lock S_LIFECYCLE_LOCK = new();

    private static AssetLoader? s_loader;
    private static AssetFileSystem? s_fileSystem;
    private static int s_ownerThreadId;
    private static long s_revision;
    private static AssetCacheOptions s_cacheOptions;
    private static long s_lastArtifactCollectionTimestamp;

    /// <summary>Gets whether asset services are initialized.</summary>
    public static bool isInitialized { get; private set; }

    /// <summary>Gets the absolute source asset root.</summary>
    public static string assetRoot { get; private set; } = string.Empty;

    /// <summary>Gets the absolute root containing rebuildable asset database data.</summary>
    public static string libraryRoot { get; private set; } = string.Empty;

    /// <summary>Gets the absolute generated artifact root.</summary>
    public static string artifactRoot { get; private set; } = string.Empty;

    /// <summary>Occurs after an asset database transaction has committed.</summary>
    public static event Action<AssetChangeSet>? Changed;

    /// <summary>Occurs after a canonical loaded asset has been updated in place.</summary>
    public static event Action<AssetObject>? AssetReloaded;

    /// <summary>Initializes global asset services.</summary>
    /// <param name="options">The asset source, artifact and watcher configuration.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when identity, type cache or serialization services are not initialized.
    /// </exception>
    [ScriptingApiIgnore]
    public static void Initialize(AssetManagerOptions options)
    {
        if (!IdentityManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires IdentityManager to be initialized first.");
        if (!TypeCacheManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires TypeCacheManager to be initialized first.");
        if (!SerializationManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires SerializationManager to be initialized first.");
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.libraryRoot))
            throw new ArgumentException("Library root is required.", nameof(options));

        lock (S_LIFECYCLE_LOCK)
        {
            ShutdownLocked();
            assetRoot = Path.GetFullPath(options.assetRoot);
            libraryRoot = Path.GetFullPath(options.libraryRoot);
            artifactRoot = Path.Combine(libraryRoot, "Artifacts");
            AssetLoader loader = new(assetRoot, libraryRoot, options.sourcePolicy);
            AssetFileSystem fileSystem = new(
                assetRoot,
                autoStart: false,
                options.fileWatcherFlushDelayMs,
                options.sourcePolicy);
            loader.AssetReloaded += OnAssetReloaded;
            s_loader = loader;
            s_fileSystem = fileSystem;
            s_ownerThreadId = Environment.CurrentManagedThreadId;
            s_revision = 0;
            s_cacheOptions = options.cacheOptions;
            s_lastArtifactCollectionTimestamp = 0;
            isInitialized = true;
            AssetSerializationServices.SetReferenceResolver(ResolveSerializedReference);
            try
            {
                loader.Rescan();
                CollectArtifactsIfDue(loader, force: true);
                fileSystem.Refresh();
                if (options.enableFileSystemWatcher)
                    fileSystem.Start();
            }
            catch
            {
                ShutdownLocked();
                throw;
            }
        }
    }

    /// <summary>Shuts down global asset services and releases all runtime resources.</summary>
    public static void Shutdown()
    {
        lock (S_LIFECYCLE_LOCK)
            ShutdownLocked();
    }

    /// <summary>Loads a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns>The canonical asset instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible asset can be loaded.</exception>
    public static TAsset Load<TAsset>(string relativePath) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(relativePath, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{relativePath}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Loads a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <returns>The canonical asset instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no compatible asset can be loaded.</exception>
    public static TAsset Load<TAsset>(Guid persistentId) where TAsset : AssetObject
    {
        AssetObject? asset = GetLoader().Load(persistentId, typeof(TAsset));
        return asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>Tries to load a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(string relativePath, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(relativePath, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>Tries to load a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset) where TAsset : AssetObject
    {
        bool success = GetLoader().TryLoad(persistentId, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        return success && asset is not null;
    }

    /// <summary>Asynchronously loads a canonical asset by source-relative path.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset instance.</returns>
    public static ValueTask<TAsset> LoadAsync<TAsset>(
        string relativePath,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        EnsureOwnerThread();
        cancellationToken.ThrowIfCancellationRequested();
        AssetObject? asset = GetLoader().Load(relativePath, typeof(TAsset));
        TAsset result = asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{relativePath}' cannot be loaded as '{typeof(TAsset).FullName}'.");
        return ValueTask.FromResult(result);
    }

    /// <summary>Asynchronously loads a canonical asset by persistent identity.</summary>
    /// <typeparam name="TAsset">The required asset type.</typeparam>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset instance.</returns>
    public static ValueTask<TAsset> LoadAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        EnsureOwnerThread();
        cancellationToken.ThrowIfCancellationRequested();
        AssetObject? asset = GetLoader().Load(persistentId, typeof(TAsset));
        TAsset result = asset as TAsset ?? throw new InvalidOperationException(
            $"Asset '{persistentId}' cannot be loaded as '{typeof(TAsset).FullName}'.");
        return ValueTask.FromResult(result);
    }

    /// <summary>Imports one source asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns><see langword="true"/> when an importer handled the source.</returns>
    public static bool Import(string relativePath)
    {
        EnsureOwnerThread();
        bool imported = GetLoader().Import(relativePath);
        if (imported)
            GetFileSystem().Refresh();
        return imported;
    }

    /// <summary>Saves an asset to its current source path.</summary>
    /// <param name="asset">The asset to save.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public static bool Save(AssetObject asset)
    {
        EnsureOwnerThread();
        bool saved = GetLoader().Save(asset);
        if (saved)
            GetFileSystem().Refresh();
        return saved;
    }

    /// <summary>Saves an unsaved asset to its initial source-relative path.</summary>
    /// <param name="relativePath">The initial source-relative path.</param>
    /// <param name="asset">The asset to save.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public static bool Save(string relativePath, AssetObject asset)
    {
        EnsureOwnerThread();
        bool saved = GetLoader().Save(relativePath, asset);
        if (saved)
            GetFileSystem().Refresh();
        return saved;
    }

    /// <summary>
    /// Moves a source asset while preserving its persistent identity and generated metadata.
    /// </summary>
    /// <param name="sourceRelativePath">Existing source-relative path.</param>
    /// <param name="targetRelativePath">New source-relative path.</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist.</exception>
    /// <exception cref="IOException">Thrown when the target source or metadata already exists.</exception>
    public static void Move(string sourceRelativePath, string targetRelativePath)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(sourceRelativePath, nameof(sourceRelativePath));
        string targetPath = NormalizeMutationPath(targetRelativePath, nameof(targetRelativePath));
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
            InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref s_revision), committed));
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
    /// <param name="relativePath">Existing source-relative file or directory path.</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist.</exception>
    public static void Delete(string relativePath)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(relativePath, nameof(relativePath));
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
        InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref s_revision), committed));
        CollectArtifactsIfDue(loader, force: true);
    }

    /// <summary>Creates a tracked source directory and its persistent metadata.</summary>
    /// <param name="relativePath">New source-relative directory path.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when the parent directory does not exist.</exception>
    /// <exception cref="IOException">Thrown when the target already exists.</exception>
    public static void CreateDirectory(string relativePath)
    {
        EnsureOwnerThread();
        string sourcePath = NormalizeMutationPath(relativePath, nameof(relativePath));
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
            InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref s_revision), committed));
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

    /// <summary>Reconciles source files, generated files and the persistent catalog.</summary>
    public static void Rescan()
    {
        EnsureOwnerThread();
        GetLoader().Rescan();
        GetFileSystem().Refresh();
    }

    /// <summary>Applies queued source and build changes on the initialization thread.</summary>
    public static void Update()
    {
        EnsureOwnerThread();
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

    /// <summary>Collects assets that have no external managed references.</summary>
    /// <returns>The number of released canonical assets.</returns>
    public static int UnloadUnusedAssets() => GetLoader().UnloadUnusedAssets();

    /// <summary>Tries to resolve an asset type without loading the asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="assetType">The resolved concrete asset type.</param>
    /// <returns><see langword="true"/> when the type can be resolved.</returns>
    public static bool TryGetAssetType(string relativePath, out Type? assetType)
        => GetLoader().TryGetAssetType(relativePath, out assetType);

    /// <summary>Tries to resolve a persistent identity without loading the asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="persistentId">The resolved persistent identity.</param>
    /// <returns><see langword="true"/> when catalog metadata exists.</returns>
    public static bool TryGetPersistentId(string relativePath, out Guid persistentId)
        => GetLoader().TryGetPersistentId(relativePath, out persistentId);

    /// <summary>Tries to get a catalog snapshot by source-relative path.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="info">The catalog snapshot when available.</param>
    /// <returns><see langword="true"/> when the path is cataloged.</returns>
    public static bool TryGetInfo(string relativePath, out AssetInfo? info)
        => GetLoader().TryGetInfo(relativePath, out info);

    /// <summary>Tries to get a catalog snapshot by persistent identity.</summary>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="info">The catalog snapshot when available.</param>
    /// <returns><see langword="true"/> when the identity is cataloged.</returns>
    public static bool TryGetInfo(Guid persistentId, out AssetInfo? info)
        => GetLoader().TryGetInfo(persistentId, out info);

    /// <summary>Tries to resolve a named artifact output.</summary>
    /// <param name="persistentId">The artifact owner identity.</param>
    /// <param name="outputName">The named output.</param>
    /// <param name="artifact">The output information when available.</param>
    /// <returns><see langword="true"/> when the output exists.</returns>
    public static bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
        => GetLoader().TryGetArtifact(persistentId, outputName, out artifact);

    /// <summary>Runs an aggregate asset build using the processor registered for a definition.</summary>
    /// <param name="definition">The build definition asset.</param>
    /// <param name="inputs">The immutable input catalog snapshots.</param>
    /// <param name="cancellationToken">Cancellation for the candidate build.</param>
    /// <returns>The content-addressed output bundle key.</returns>
    public static ValueTask<AssetArtifactKey> BuildAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnerThread();
        return GetLoader().BuildAsync(definition, inputs, cancellationToken);
    }

    /// <summary>Gets source paths for all canonical loaded assets.</summary>
    /// <returns>A stable source-relative path snapshot.</returns>
    public static IReadOnlyList<string> GetLoadedPaths() => GetLoader().GetLoadedPaths();

    /// <summary>Gets direct or transitive runtime dependencies of an asset.</summary>
    /// <param name="asset">The asset to query.</param>
    /// <param name="recursive">Whether transitive dependencies should be included.</param>
    /// <returns>The dependency descriptors.</returns>
    public static IReadOnlyList<AssetDependency> GetDependencies(
        AssetObject asset,
        bool recursive = false)
        => GetLoader().GetDependencies(asset, recursive);

    /// <summary>Gets an engine-known reference diagnostic snapshot.</summary>
    /// <param name="asset">The asset to inspect.</param>
    /// <returns>The reference diagnostic snapshot.</returns>
    public static AssetReferenceInfo GetReferenceInfo(AssetObject asset)
        => GetLoader().GetReferenceInfo(asset);

    /// <summary>Gets indexed source entries.</summary>
    /// <param name="includeDirectories">Whether directories should be included.</param>
    /// <returns>The stable source entry snapshot.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
        => GetFileSystem().GetEntries(includeDirectories);

    /// <summary>Gets immediate indexed children of a source directory.</summary>
    /// <param name="parentRelativePath">The source-relative parent path.</param>
    /// <returns>The immediate child entry snapshot.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemChildren(string parentRelativePath)
        => GetFileSystem().GetChildren(parentRelativePath);

    /// <summary>Tries to resolve an indexed source entry.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="entry">The resolved source entry.</param>
    /// <returns><see langword="true"/> when the entry exists and is not generated metadata.</returns>
    public static bool TryGetFileSystemEntry(string relativePath, out AssetFileEntry entry)
    {
        return GetFileSystem().TryGetEntry(relativePath, out entry);
    }

    /// <summary>Waits until queued source watcher changes have been processed.</summary>
    public static void WaitForIdle()
    {
        EnsureOwnerThread();
        AssetFileSystem fileSystem = GetFileSystem();
        IReadOnlyList<AssetChangedEvent> changes = fileSystem.WaitForIdle(out bool requiresFullRescan);
        if (changes.Count > 0 || requiresFullRescan)
            ApplySourceChanges(changes, requiresFullRescan);
        GetLoader().WaitForIdle();
        CollectArtifactsIfDue(GetLoader(), force: true);
    }

    internal static AssetObject ResolveSerializedReference(
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
                $"Asset reference '{persistentId}' at '{propertyPath}' cannot be resolved as " +
                $"'{expectedType.FullName}'.",
                exception);
        }
    }

    private static void ApplySourceChanges(
        IReadOnlyList<AssetChangedEvent> changes,
        bool requiresFullRescan)
    {
        AssetLoader? loader = s_loader;
        if (loader is null)
            return;
        Dictionary<string, Guid> previousIds = CapturePreviousIds(loader, changes);
        try
        {
            if (requiresFullRescan)
                loader.Rescan();
            else
                loader.ApplySourceChanges(changes);
            AssetManagerDiagnosticPublisher.ResolveSourceDatabase();
        }
        catch (Exception exception)
        {
            try
            {
                loader.Rescan();
                AssetManagerDiagnosticPublisher.ResolveSourceDatabase();
                Log.Warn(
                    "Asset source refresh failed and was recovered by a full rescan: {0}",
                    exception);
            }
            catch (Exception recoveryException)
            {
                Log.Error(
                    "Asset source refresh and recovery rescan both failed. Refresh: {0} Recovery: {1}",
                    exception,
                    recoveryException);
                AssetManagerDiagnosticPublisher.PublishSourceDatabaseFailure(
                    exception,
                    recoveryException);
            }
        }
        AssetChange[] committed = CreateCommittedChanges(loader, changes, previousIds, requiresFullRescan);
        InvokeObservers(Changed, new AssetChangeSet(Interlocked.Increment(ref s_revision), committed));
    }

    private static void DrainPendingChanges(AssetFileSystem fileSystem)
    {
        if (!fileSystem.isWatching)
            return;
        IReadOnlyList<AssetChangedEvent> pending = fileSystem.WaitForIdle(out bool requiresFullRescan);
        if (pending.Count > 0 || requiresFullRescan)
            ApplySourceChanges(pending, requiresFullRescan);
    }

    private static AssetChangedEvent[] CreateDeletionEvents(
        AssetFileSystem fileSystem,
        string sourcePath)
    {
        string prefix = sourcePath + "/";
        return fileSystem.GetEntries()
            .Where(entry =>
                string.Equals(entry.relativePath, sourcePath, StringComparison.OrdinalIgnoreCase) ||
                entry.relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.relativePath.Length)
            .Select(static entry => new AssetChangedEvent(entry.relativePath, WatcherChangeTypes.Deleted))
            .DefaultIfEmpty(new AssetChangedEvent(sourcePath, WatcherChangeTypes.Deleted))
            .ToArray();
    }

    private static void OnAssetReloaded(AssetObject asset)
        => InvokeObservers(AssetReloaded, asset);

    private static void InvokeObservers<T>(Action<T>? handlers, T value)
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

    private static AssetLoader GetLoader()
        => isInitialized && s_loader is not null
            ? s_loader
            : throw new InvalidOperationException("AssetManager is not initialized.");

    private static AssetFileSystem GetFileSystem()
        => isInitialized && s_fileSystem is not null
            ? s_fileSystem
            : throw new InvalidOperationException("AssetManager is not initialized.");

    private static Dictionary<string, Guid> CapturePreviousIds(
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
            if (loader.TryGetPersistentId(path, out Guid id))
                result[path] = id;
        }
        return result;
    }

    private static AssetChange[] CreateCommittedChanges(
        AssetLoader loader,
        IReadOnlyList<AssetChangedEvent> changes,
        IReadOnlyDictionary<string, Guid> previousIds,
        bool requiresFullRescan)
    {
        if (requiresFullRescan && changes.Count == 0)
            return [new AssetChange(AssetChangeKind.StatusChanged, Guid.Empty, string.Empty)];

        var result = new List<AssetChange>(changes.Count);
        for (int i = 0; i < changes.Count; i++)
        {
            AssetChangedEvent change = changes[i];
            bool moved = change.changeType.HasFlag(WatcherChangeTypes.Renamed);
            bool deleted = change.changeType.HasFlag(WatcherChangeTypes.Deleted);
            Guid id = Guid.Empty;
            if (!loader.TryGetPersistentId(change.relativePath, out id))
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
            result.Add(new AssetChange(kind, id, change.relativePath, change.oldRelativePath));
        }
        return result.ToArray();
    }

    private static void EnsureOwnerThread()
    {
        if (!isInitialized)
            throw new InvalidOperationException("AssetManager is not initialized.");
        if (Environment.CurrentManagedThreadId != s_ownerThreadId)
        {
            throw new InvalidOperationException(
                "Asset database mutations must run on the thread that initialized AssetManager.");
        }
    }

    private static string NormalizeMutationPath(string relativePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Asset source path is required.", parameterName);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Asset source paths must be relative.", parameterName);
        string normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment == ".."))
        {
            throw new ArgumentException("Asset source paths cannot escape the configured root.", parameterName);
        }
        return normalized;
    }

    private static void MovePhysicalSource(string sourcePath, string targetPath, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(sourcePath, targetPath);
        else
            System.IO.File.Move(sourcePath, targetPath);
    }

    private static void TryRollbackPhysicalMove(string sourcePath, string targetPath, bool isDirectory)
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

    private static void TryRollbackMetadataMove(string sourcePath, string targetPath)
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

    private static void RestoreStagedDeletion(
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

    private static void RestoreStagedMetadata(string metaPath, string stagedMetaPath)
    {
        if (!System.IO.File.Exists(stagedMetaPath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        System.IO.File.Move(stagedMetaPath, metaPath);
    }

    private static void DeleteTransactionDirectorySafely(string transactionRoot)
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

    private static void CollectArtifactsIfDue(AssetLoader loader, bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now - s_lastArtifactCollectionTimestamp < 60_000)
            return;
        s_lastArtifactCollectionTimestamp = now;
        _ = loader.CollectArtifacts(
            s_cacheOptions.garbageCollectionGracePeriod,
            s_cacheOptions.maximumSizeBytes);
    }

    private static void ShutdownLocked()
    {
        AssetManagerDiagnosticPublisher.ResolveSourceDatabase();
        AssetSerializationServices.SetReferenceResolver(null);
        if (s_fileSystem is not null)
        {
            s_fileSystem.Dispose();
        }
        if (s_loader is not null)
        {
            s_loader.AssetReloaded -= OnAssetReloaded;
            s_loader.Dispose();
        }
        s_fileSystem = null;
        s_loader = null;
        assetRoot = string.Empty;
        libraryRoot = string.Empty;
        artifactRoot = string.Empty;
        s_ownerThreadId = 0;
        s_revision = 0;
        s_cacheOptions = default;
        s_lastArtifactCollectionTimestamp = 0;
        isInitialized = false;
        Changed = null;
        AssetReloaded = null;
    }
}
