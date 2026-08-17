using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Importers;
using Inno.Assets.Loader;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Global static entry point for asset importing, caching, loading and saving.
/// </summary>
public static class AssetManager
{
    private static readonly Lock SYNC = new();

    private static AssetFileSystem s_fileSystem = null!;
    private static AssetLoader s_loader = null!;
    private static readonly Dictionary<Guid, int> s_manualHoldCountById = new();
    private static readonly Dictionary<Guid, int> s_rootHoldCountById = new();
    private static readonly Dictionary<Guid, int> s_aggregateHoldCountById = new();
    private static readonly Dictionary<Guid, Guid[]> s_dependencyClosureByRootId = new();
    private static readonly Dictionary<Guid, HashSet<Guid>> s_assetRootsByOwnerId = new();
    private static readonly Dictionary<Guid, WeakReference<AssetObject>> s_missingAssetById = new();
    private static readonly HashSet<Type> s_discoveredImporterTypes = [];
    private static readonly HashSet<Type> s_manualImporterAssetTypes = [];

    #region State

    /// <summary>
    /// Absolute source asset root directory.
    /// </summary>
    public static string assetRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Absolute imported artifact root directory.
    /// </summary>
    public static string artifactRoot { get; private set; } = string.Empty;

    /// <summary>
    /// True when manager has been initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Raised when source files changed in asset root.
    /// </summary>
    public static event Action<IReadOnlyList<AssetChangedEvent>>? SourceFileSystemChanged;

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initializes asset manager.
    /// </summary>
    /// <param name="options">Source, artifact, and file-watcher configuration.</param>
    /// <exception cref="ArgumentException">Thrown when a required root path is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Identity, TypeCache, or Serialization services have not been initialized.
    /// </exception>
    public static void Initialize(AssetManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(options));
        if (!IdentityManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires IdentityManager to be initialized first.");
        if (!TypeCacheManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires TypeCacheManager to be initialized first.");
        if (!SerializationManager.isInitialized)
            throw new InvalidOperationException("AssetManager requires SerializationManager to be initialized first.");

        // Ensure the built-in importer package is loaded before querying TypeCache.
        _ = typeof(BinaryAssetImporter).Assembly;

        try
        {
            lock (SYNC)
            {
                ShutdownInternal();

                assetRoot = Path.GetFullPath(options.assetRoot);
                artifactRoot = Path.GetFullPath(options.artifactRoot);
                Directory.CreateDirectory(assetRoot);
                Directory.CreateDirectory(artifactRoot);

                s_loader = new AssetLoader(assetRoot, artifactRoot);
                s_fileSystem = new AssetFileSystem(assetRoot, autoStart: false, options.fileWatcherFlushDelayMs);
                s_fileSystem.ChangedBatch += OnFileSystemChangedBatch;
                IdentityManager.ObjectUnregistered += OnIdentityObjectUnregistered;

                isInitialized = true;
            }

            RefreshImportersFromTypeCache();

            lock (SYNC)
            {
                s_loader.ReconcileStorageState();
                s_fileSystem.Refresh();
                if (options.enableFileSystemWatcher)
                    s_fileSystem.Start();
            }
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    /// <summary>
    /// Clears caches and unregisters importers.
    /// </summary>
    public static void Shutdown()
    {
        lock (SYNC)
        {
            ShutdownInternal();
        }
    }

    #endregion

    #region Importers

    /// <summary>
    /// Registers an importer by type using parameterless constructor.
    /// </summary>
    /// <typeparam name="TImporter">Importer type to construct and register.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when AssetManager is not initialized.</exception>
    public static void RegisterImporter<TImporter>() where TImporter : IAssetImporter, new()
    {
        RegisterImporter(new TImporter());
    }

    /// <summary>
    /// Registers an importer instance.
    /// </summary>
    /// <param name="importer">Importer instance to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="importer"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when AssetManager is not initialized.</exception>
    public static void RegisterImporter(IAssetImporter importer)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(importer);
        lock (SYNC)
            s_manualImporterAssetTypes.Add(importer.targetAssetType);
        s_loader.RegisterImporter(importer);
    }

    #endregion

    #region Importing

    /// <summary>
    /// Imports the assets from disk for generating metadata and artifacts.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <returns><see langword="true"/> when a compatible importer produced metadata and an artifact.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the imported dependency graph contains a cycle.</exception>
    public static bool Import(string relativePath)
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        return s_loader.Import(relativePath);
    }

    /// <summary>
    /// Re-scans the full source asset tree, repairs stale generated files and syncs loaded assets.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when AssetManager is not initialized.</exception>
    public static void Rescan()
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        lock (SYNC)
        {
            s_loader.ReconcileStorageState();
            s_fileSystem.Refresh();
        }
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads an asset from existing metadata and artifact files into memory.
    /// </summary>
    /// <typeparam name="TAsset">Required asset type.</typeparam>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <returns>The loaded asset.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the asset cannot be loaded as the requested type.</exception>
    public static TAsset Load<TAsset>(string relativePath)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        TAsset asset = s_loader.Load(relativePath, typeof(TAsset)) as TAsset
            ?? throw new InvalidOperationException($"Asset '{relativePath}' could not be loaded as '{typeof(TAsset).FullName}'.");
        AddManualHold(asset);
        return asset;
    }

    /// <summary>
    /// Tries to load an asset from existing metadata and artifact files.
    /// </summary>
    /// <typeparam name="TAsset">Required asset type.</typeparam>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="asset">Loaded asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(string relativePath, out TAsset? asset)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        bool loaded = s_loader.TryLoad(relativePath, typeof(TAsset), out AssetObject? value);
        asset = value as TAsset;
        if (!loaded || asset is null)
            return false;
        AddManualHold(asset);
        return true;
    }

    /// <summary>
    /// Loads an asset through its persistent identity and establishes one manual hold.
    /// </summary>
    /// <typeparam name="TAsset">Required asset type.</typeparam>
    /// <param name="persistentId">Persistent asset identity.</param>
    /// <returns>The shared loaded asset instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="persistentId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the asset cannot be resolved as the requested type.</exception>
    public static TAsset Load<TAsset>(Guid persistentId)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        if (persistentId == Guid.Empty)
            throw new ArgumentException("An asset persistent identity cannot be empty.", nameof(persistentId));

        TAsset asset = s_loader.ResolveOrLoad(new Identity(persistentId), typeof(TAsset)) as TAsset
            ?? throw new InvalidOperationException(
                $"Asset '{persistentId}' could not be loaded as '{typeof(TAsset).FullName}'.");
        AddManualHold(asset);
        return asset;
    }

    /// <summary>
    /// Tries to load an asset through its persistent identity and establishes one manual hold on success.
    /// </summary>
    /// <typeparam name="TAsset">Required asset type.</typeparam>
    /// <param name="persistentId">Persistent asset identity.</param>
    /// <param name="asset">The shared loaded asset instance when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public static bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset)
        where TAsset : AssetObject
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        if (persistentId == Guid.Empty)
        {
            asset = null;
            return false;
        }

        asset = s_loader.ResolveOrLoad(new Identity(persistentId), typeof(TAsset)) as TAsset;
        if (asset is null)
            return false;
        AddManualHold(asset);
        return true;
    }

    /// <summary>
    /// Registers an asset root and its transitive dependencies for an identity owner.
    /// </summary>
    /// <param name="owner">Identity owner whose lifetime retains the dependency graph.</param>
    /// <param name="rootAsset">Root asset to retain.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the owner is not registered or either object has no persistent identity.
    /// </exception>
    public static void TrackDependencies(IIdentityObject owner, AssetObject rootAsset)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(rootAsset);

        Identity ownerIdentity = owner.GetIdentity();
        Guid ownerId = ownerIdentity.persistentId;
        Guid rootId = rootAsset.identity.persistentId;
        if (ownerId == Guid.Empty)
            throw new InvalidOperationException($"Dependency owner '{owner.GetType().FullName}' has no persistent identity.");
        if (ownerIdentity.runtimeId is null)
        {
            throw new InvalidOperationException(
                $"Dependency owner '{owner.GetType().FullName}' ({ownerId}) must be registered with " +
                $"{nameof(IdentityManager)} before dependencies can be tracked.");
        }
        if (rootId == Guid.Empty)
            throw new InvalidOperationException($"Asset '{rootAsset.GetType().FullName}' has no persistent identity.");

        lock (SYNC)
        {
            if (s_assetRootsByOwnerId.TryGetValue(ownerId, out HashSet<Guid>? existingRoots) &&
                existingRoots.Contains(rootId))
            {
                return;
            }
        }

        AcquireRoot(rootAsset);
        bool duplicateRegistration;
        lock (SYNC)
        {
            if (!s_assetRootsByOwnerId.TryGetValue(ownerId, out HashSet<Guid>? roots))
            {
                roots = [];
                s_assetRootsByOwnerId.Add(ownerId, roots);
            }

            duplicateRegistration = !roots.Add(rootId);
        }
        if (duplicateRegistration)
            ReleaseRoot(rootId);
    }

    /// <summary>
    /// Discovers direct serialized asset references and retains each referenced dependency graph for an owner.
    /// </summary>
    /// <param name="owner">Identity owner whose lifetime retains discovered references.</param>
    /// <param name="value">Serializable value to inspect.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static void TrackSerializedReferences(IIdentityObject owner, ISerializable value)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(value);

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (AssetObject asset in EnumerateSerializedAssets(value, visited))
            TrackDependencies(owner, asset);
    }

    /// <summary>
    /// Tries to resolve an asset's concrete metadata type without loading the asset.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="assetType">Resolved concrete asset type when available.</param>
    /// <returns><see langword="true"/> when the asset type was resolved.</returns>
    public static bool TryGetAssetType(string relativePath, out Type? assetType)
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        lock (SYNC)
        {
            return s_loader.TryGetAssetType(relativePath, out assetType);
        }
    }

    /// <summary>
    /// Tries to resolve the persistent identity catalog entry for an asset path without loading the asset.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="persistentId">Persistent identity when metadata is available.</param>
    /// <returns><see langword="true"/> when a non-empty persistent identity was resolved.</returns>
    public static bool TryGetPersistentId(string relativePath, out Guid persistentId)
    {
        EnsureInitialized();
        Identity identity = s_loader.GetIdentity(relativePath);
        persistentId = identity.persistentId;
        return persistentId != Guid.Empty;
    }

    /// <summary>
    /// Returns currently loaded relative paths.
    /// </summary>
    /// <returns>A stable path snapshot in deterministic order.</returns>
    public static IReadOnlyList<string> GetLoadedPaths()
    {
        EnsureInitialized();
        return s_loader.GetLoadedPaths();
    }

    #endregion

    #region Saving

    /// <summary>
    /// Saves asset back to its current source path.
    /// </summary>
    /// <param name="asset">Asset state to export.</param>
    /// <returns><see langword="true"/> when export and reimport succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="asset"/> is null.</exception>
    public static bool Save(AssetObject asset)
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        bool saved = s_loader.Save(asset);
        if (saved)
            UnloadIfUnheld(asset.sourcePath);
        return saved;
    }

    /// <summary>
    /// Saves asset back to source path.
    /// </summary>
    /// <param name="relativePath">Destination path relative to the asset root.</param>
    /// <param name="asset">Asset state to export.</param>
    /// <returns><see langword="true"/> when export and reimport succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="asset"/> is null.</exception>
    public static bool Save(string relativePath, AssetObject asset)
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        bool saved = s_loader.Save(relativePath, asset);
        if (saved)
            UnloadIfUnheld(relativePath);
        return saved;
    }

    #endregion

    #region Unloading

    /// <summary>
    /// Unloads one asset by path.
    /// </summary>
    /// <param name="relativePath">Source path whose manual hold should be released.</param>
    /// <returns><see langword="true"/> when one manual hold was released.</returns>
    public static bool Unload(string relativePath)
    {
        if (!isInitialized)
            return false;

        Identity identity = s_loader.GetIdentity(relativePath);
        return ReleaseManualHold(identity.persistentId);
    }

    /// <summary>
    /// Releases one manual hold established for an asset instance.
    /// </summary>
    /// <param name="asset">Loaded asset whose manual hold should be released.</param>
    /// <returns><see langword="true"/> when a manual hold was released.</returns>
    public static bool Unload(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!isInitialized)
            return false;

        return ReleaseManualHold(asset.identity.persistentId);
    }

    /// <summary>
    /// Unloads all loaded assets.
    /// </summary>
    /// <remarks>Only manual holds are released. Identity-owner holds remain active.</remarks>
    public static void UnloadAll()
    {
        if (!isInitialized)
            return;

        KeyValuePair<Guid, int>[] manualHolds;
        lock (SYNC)
        {
            manualHolds = s_manualHoldCountById.ToArray();
            s_manualHoldCountById.Clear();
        }

        for (int i = 0; i < manualHolds.Length; i++)
        {
            for (int hold = 0; hold < manualHolds[i].Value; hold++)
                ReleaseRoot(manualHolds[i].Key);
        }
    }

    internal static AssetObject ResolveSerializedReference(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string serializationPath)
    {
        EnsureInitialized();
        RefreshImportersFromTypeCache();
        ArgumentNullException.ThrowIfNull(expectedType);
        if (persistentId == Guid.Empty)
            throw new InvalidDataException($"Asset reference at '{serializationPath}' has an empty persistent identity.");
        if (!typeof(AssetObject).IsAssignableFrom(expectedType))
        {
            throw new InvalidDataException(
                $"Asset reference at '{serializationPath}' declares non-asset type '{expectedType.FullName}'.");
        }

        Type concreteType = expectedType;
        if (stableTypeId != Guid.Empty)
        {
            if (!TypeCache.TryResolveType(stableTypeId, out Type? resolvedType) || resolvedType is null)
            {
                throw new InvalidDataException(
                    $"Asset reference '{persistentId}' at '{serializationPath}' uses unknown stable type id '{stableTypeId}'.");
            }

            if (!typeof(AssetObject).IsAssignableFrom(resolvedType) || !expectedType.IsAssignableFrom(resolvedType))
            {
                throw new InvalidDataException(
                    $"Asset reference '{persistentId}' at '{serializationPath}' resolves to '{resolvedType.FullName}', " +
                    $"which is incompatible with '{expectedType.FullName}'.");
            }

            concreteType = resolvedType;
        }

        AssetObject? loaded = s_loader.ResolveOrLoad(new Identity(persistentId), concreteType);
        if (loaded is not null)
            return loaded;

        lock (SYNC)
        {
            if (s_missingAssetById.TryGetValue(persistentId, out WeakReference<AssetObject>? weakReference) &&
                weakReference.TryGetTarget(out AssetObject? existingMissing) &&
                expectedType.IsAssignableFrom(existingMissing.GetType()))
            {
                return existingMissing;
            }
        }

        AssetObject missing;
        try
        {
            missing = Activator.CreateInstance(concreteType, nonPublic: true) as AssetObject
                ?? throw new InvalidOperationException("Activator returned null.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Missing asset '{persistentId}' at '{serializationPath}' requires asset type " +
                $"'{concreteType.FullName}' to have a parameterless constructor.",
                exception);
        }

        missing.InitializeMissing(persistentId, lastKnownPath);
        lock (SYNC)
            s_missingAssetById[persistentId] = new WeakReference<AssetObject>(missing);
        return missing;
    }

    private static void AddManualHold(AssetObject asset)
    {
        Guid persistentId = asset.identity.persistentId;
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException("A loaded asset must have a persistent identity.");

        AcquireRoot(asset);
        lock (SYNC)
        {
            s_manualHoldCountById.TryGetValue(persistentId, out int count);
            s_manualHoldCountById[persistentId] = count + 1;
        }
    }

    private static bool ReleaseManualHold(Guid persistentId)
    {
        lock (SYNC)
        {
            if (!s_manualHoldCountById.TryGetValue(persistentId, out int manualCount))
                return false;
            if (manualCount <= 1)
                s_manualHoldCountById.Remove(persistentId);
            else
                s_manualHoldCountById[persistentId] = manualCount - 1;
        }

        ReleaseRoot(persistentId);
        return true;
    }

    private static void AcquireRoot(AssetObject rootAsset)
    {
        Guid rootId = rootAsset.identity.persistentId;
        Guid[] resolvedClosure = BuildDependencyClosure(rootAsset);
        Guid[] unloadCandidates = [];

        lock (SYNC)
        {
            if (!s_dependencyClosureByRootId.TryGetValue(rootId, out Guid[]? closure))
            {
                closure = resolvedClosure;
                s_dependencyClosureByRootId.Add(rootId, resolvedClosure);
            }
            else if (!closure.SequenceEqual(resolvedClosure))
            {
                s_rootHoldCountById.TryGetValue(rootId, out int existingRootHolds);
                var resolvedIds = resolvedClosure.ToHashSet();
                var previousIds = closure.ToHashSet();
                var unload = new List<Guid>();

                foreach (Guid removedId in previousIds.Except(resolvedIds))
                {
                    if (!s_aggregateHoldCountById.TryGetValue(removedId, out int count))
                        continue;
                    int remaining = count - existingRootHolds;
                    if (remaining <= 0)
                    {
                        s_aggregateHoldCountById.Remove(removedId);
                        unload.Add(removedId);
                    }
                    else
                    {
                        s_aggregateHoldCountById[removedId] = remaining;
                    }
                }

                foreach (Guid addedId in resolvedIds.Except(previousIds))
                {
                    s_aggregateHoldCountById.TryGetValue(addedId, out int count);
                    s_aggregateHoldCountById[addedId] = count + existingRootHolds;
                }

                closure = resolvedClosure;
                s_dependencyClosureByRootId[rootId] = resolvedClosure;
                unloadCandidates = unload.ToArray();
            }

            s_rootHoldCountById.TryGetValue(rootId, out int rootHoldCount);
            s_rootHoldCountById[rootId] = rootHoldCount + 1;
            for (int i = 0; i < closure.Length; i++)
            {
                s_aggregateHoldCountById.TryGetValue(closure[i], out int aggregateCount);
                s_aggregateHoldCountById[closure[i]] = aggregateCount + 1;
            }
        }

        for (int i = 0; i < unloadCandidates.Length; i++)
            s_loader.Unload(new Identity(unloadCandidates[i]));
    }

    private static void ReleaseRoot(Guid rootId)
    {
        Guid[] unloadCandidates;
        lock (SYNC)
        {
            if (!s_rootHoldCountById.TryGetValue(rootId, out int rootHoldCount) ||
                !s_dependencyClosureByRootId.TryGetValue(rootId, out Guid[]? closure))
            {
                return;
            }

            var unload = new List<Guid>();
            for (int i = closure.Length - 1; i >= 0; i--)
            {
                Guid assetId = closure[i];
                if (!s_aggregateHoldCountById.TryGetValue(assetId, out int aggregateCount))
                    continue;
                if (aggregateCount <= 1)
                {
                    s_aggregateHoldCountById.Remove(assetId);
                    unload.Add(assetId);
                }
                else
                {
                    s_aggregateHoldCountById[assetId] = aggregateCount - 1;
                }
            }

            if (rootHoldCount <= 1)
            {
                s_rootHoldCountById.Remove(rootId);
                s_dependencyClosureByRootId.Remove(rootId);
            }
            else
            {
                s_rootHoldCountById[rootId] = rootHoldCount - 1;
            }

            unloadCandidates = unload.ToArray();
        }

        for (int i = 0; i < unloadCandidates.Length; i++)
            s_loader.Unload(new Identity(unloadCandidates[i]));
    }

    #endregion

    #region File System

    /// <summary>
    /// Returns current filesystem tree graph for source assets.
    /// </summary>
    /// <returns>A human-readable source tree.</returns>
    public static string GetFileSystemTreeGraph()
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.BuildTreeGraph();
        }
    }

    /// <summary>
    /// Returns a snapshot of indexed source files/directories.
    /// </summary>
    /// <param name="includeDirectories">Whether directory entries should be included.</param>
    /// <returns>A stable file-system entry snapshot.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemEntries(bool includeDirectories = true)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.GetEntries(includeDirectories);
        }
    }

    /// <summary>
    /// Returns a snapshot of immediate children under one directory path.
    /// </summary>
    /// <param name="parentRelativePath">Parent directory relative to the asset root.</param>
    /// <returns>A stable snapshot of immediate children.</returns>
    public static IReadOnlyList<AssetFileEntry> GetFileSystemChildren(string parentRelativePath)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.GetChildren(parentRelativePath);
        }
    }

    /// <summary>
    /// Tries to get one indexed source file-system entry by relative path.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="entry">Resolved entry when available.</param>
    /// <returns><see langword="true"/> when an entry exists.</returns>
    public static bool TryGetFileSystemEntry(string relativePath, out AssetFileEntry entry)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.TryGetEntry(relativePath, out entry);
        }
    }

    /// <summary>
    /// Waits until the filesystem async tasks to synchronize.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when AssetManager is not initialized.</exception>
    public static void WaitForIdle()
    {
        EnsureInitialized();

        if (!s_fileSystem.isWatching)
            return;

        s_fileSystem.WaitForIdle();
    }

    #endregion

    #region File System Events

    private static void OnFileSystemChangedBatch(IReadOnlyList<AssetChangedEvent> changes)
    {
        RefreshImportersFromTypeCache();
        AssetChangedEvent[] sourceChanges;
        lock (SYNC)
        {
            sourceChanges = changes
                .Where(static x => !s_loader.IsInternalGeneratedPath(x.relativePath))
                .ToArray();

            if (sourceChanges.Length == 0)
                return;

            AssetChangedEvent[] renamed = sourceChanges
                .Where(static x => x.changeType.HasFlag(WatcherChangeTypes.Renamed))
                .ToArray();
            AssetChangedEvent[] deleted = sourceChanges
                .Where(static x => !x.changeType.HasFlag(WatcherChangeTypes.Renamed) && x.changeType.HasFlag(WatcherChangeTypes.Deleted))
                .ToArray();
            AssetChangedEvent[] createdOrChanged = sourceChanges
                .Where(static x => !x.changeType.HasFlag(WatcherChangeTypes.Renamed) && !x.changeType.HasFlag(WatcherChangeTypes.Deleted))
                .ToArray();

            for (int i = 0; i < renamed.Length; i++)
                s_loader.HandleRenamedSourcePath(renamed[i].oldRelativePath, renamed[i].relativePath);

            for (int i = 0; i < deleted.Length; i++)
                s_loader.HandleDeletedSourcePath(deleted[i].relativePath);

            for (int i = 0; i < createdOrChanged.Length; i++)
                s_loader.HandleCreatedOrChangedSourcePath(createdOrChanged[i].relativePath);
        }

        SourceFileSystemChanged?.Invoke(sourceChanges);
    }

    #endregion

    #region Internals

    private static Guid[] BuildDependencyClosure(AssetObject rootAsset)
    {
        var ordered = new List<Guid>();
        var completed = new HashSet<Guid>();
        var active = new List<Guid>();

        Visit(rootAsset);
        return ordered.ToArray();

        void Visit(AssetObject asset)
        {
            Guid assetId = asset.identity.persistentId;
            if (completed.Contains(assetId))
                return;
            int cycleStart = active.IndexOf(assetId);
            if (cycleStart >= 0)
            {
                string cycle = string.Join(" -> ", active.Skip(cycleStart).Append(assetId));
                throw new InvalidOperationException($"Asset dependency cycle detected: {cycle}.");
            }

            active.Add(assetId);
            for (int i = 0; i < asset.dependencies.Count; i++)
            {
                AssetDependency dependency = asset.dependencies[i];
                if (dependency.persistentId == Guid.Empty)
                    continue;

                AssetObject? dependencyAsset = ResolveDependencyAsset(dependency);
                if (dependencyAsset is not null)
                    Visit(dependencyAsset);
                else if (completed.Add(dependency.persistentId))
                    ordered.Add(dependency.persistentId);
            }

            active.RemoveAt(active.Count - 1);
            if (completed.Add(assetId))
                ordered.Add(assetId);
        }
    }

    private static AssetObject? ResolveDependencyAsset(AssetDependency dependency)
    {
        Type requestedType = typeof(AssetObject);
        if (dependency.stableTypeId != Guid.Empty &&
            TypeCache.TryResolveType(dependency.stableTypeId, out Type? resolvedType) &&
            resolvedType is not null &&
            typeof(AssetObject).IsAssignableFrom(resolvedType))
        {
            requestedType = resolvedType;
        }

        AssetObject? asset = s_loader.ResolveOrLoad(new Identity(dependency.persistentId), requestedType);
        if (asset is not null)
            return asset;
        if (string.IsNullOrWhiteSpace(dependency.lastKnownPath))
            return null;
        return s_loader.Load(dependency.lastKnownPath, requestedType);
    }

    private static IEnumerable<AssetObject> EnumerateSerializedAssets(
        object? value,
        HashSet<object> visited)
    {
        if (value is null || value is string || value is Type)
            yield break;
        if (value is AssetObject asset)
        {
            yield return asset;
            yield break;
        }

        Type valueType = value.GetType();
        if (valueType.IsPrimitive || valueType.IsEnum || value is decimal || value is Guid)
            yield break;
        if (!valueType.IsValueType && !visited.Add(value))
            yield break;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                foreach (AssetObject nested in EnumerateSerializedAssets(entry.Key, visited))
                    yield return nested;
                foreach (AssetObject nested in EnumerateSerializedAssets(entry.Value, visited))
                    yield return nested;
            }
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (object? element in enumerable)
            {
                foreach (AssetObject nested in EnumerateSerializedAssets(element, visited))
                    yield return nested;
            }
            yield break;
        }

        if (value is ISerializable serializable)
        {
            foreach (MemberInfo member in GetSerializableMembers(serializable.GetType()))
            {
                object? memberValue = member switch
                {
                    FieldInfo field => field.GetValue(serializable),
                    PropertyInfo property => property.GetValue(serializable),
                    _ => null
                };
                foreach (AssetObject nested in EnumerateSerializedAssets(memberValue, visited))
                    yield return nested;
            }
            yield break;
        }

        if (!valueType.IsValueType)
            yield break;

        const BindingFlags memberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in valueType.GetFields(memberFlags))
        {
            if (field.IsStatic || (!field.IsPublic && field.GetCustomAttribute<SerializablePropertyAttribute>() is null))
                continue;
            foreach (AssetObject nested in EnumerateSerializedAssets(field.GetValue(value), visited))
                yield return nested;
        }

        foreach (PropertyInfo property in valueType.GetProperties(memberFlags))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null || property.GetMethod.IsStatic)
                continue;
            SerializablePropertyAttribute? attribute = property.GetCustomAttribute<SerializablePropertyAttribute>();
            bool isPublicRestorableProperty =
                property.GetMethod.IsPublic && property.SetMethod?.IsPublic == true;
            if (!isPublicRestorableProperty && attribute is null)
                continue;
            foreach (AssetObject nested in EnumerateSerializedAssets(property.GetValue(value), visited))
                yield return nested;
        }
    }

    private static IEnumerable<MemberInfo> GetSerializableMembers(Type valueType)
    {
        const BindingFlags memberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var hierarchy = new Stack<Type>();
        for (Type? current = valueType; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Push(current);
        while (hierarchy.Count != 0)
        {
            foreach (MemberInfo member in hierarchy.Pop()
                         .GetMembers(memberFlags)
                         .OrderBy(static item => item.MetadataToken))
            {
                SerializablePropertyAttribute? attribute =
                    member.GetCustomAttribute<SerializablePropertyAttribute>(inherit: true);
                if (attribute is null ||
                    (attribute.propertyVisibility & PropertyVisibility.Serialize) == 0)
                {
                    continue;
                }
                if (member is PropertyInfo property &&
                    (property.GetIndexParameters().Length != 0 ||
                     property.GetGetMethod(nonPublic: true) is null))
                {
                    continue;
                }
                if (member is FieldInfo or PropertyInfo)
                    yield return member;
            }
        }
    }

    private static void OnIdentityObjectUnregistered(IIdentityObject owner)
    {
        Guid ownerId = owner.GetIdentity().persistentId;
        Guid[] roots;
        lock (SYNC)
        {
            if (!s_assetRootsByOwnerId.Remove(ownerId, out HashSet<Guid>? ownerRoots))
                return;
            roots = ownerRoots.ToArray();
        }

        for (int i = 0; i < roots.Length; i++)
            ReleaseRoot(roots[i]);
    }

    private static void UnloadIfUnheld(string relativePath)
    {
        Identity identity = s_loader.GetIdentity(relativePath);
        lock (SYNC)
        {
            if (s_aggregateHoldCountById.ContainsKey(identity.persistentId))
                return;
        }
        s_loader.Unload(relativePath);
    }

    private static void EnsureInitialized()
    {
        if (!isInitialized)
        {
            Log.Error("Asset Manager not initialized");
            throw new InvalidOperationException("AssetManager is not initialized.");
        }
    }

    private static void RefreshImportersFromTypeCache()
    {
        Type[] importerTypes = TypeCache.GetTypesImplementing<IAssetImporter>()
            .OrderBy(static type => type.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        lock (SYNC)
        {
            if (importerTypes.Length == s_discoveredImporterTypes.Count &&
                importerTypes.All(s_discoveredImporterTypes.Contains))
            {
                return;
            }
        }

        var importerTypeByAssetType = new Dictionary<Type, Type>();
        var importers = new List<(Type importerType, IAssetImporter importer)>();
        HashSet<Type> manualAssetTypes;
        lock (SYNC)
            manualAssetTypes = [.. s_manualImporterAssetTypes];

        for (int i = 0; i < importerTypes.Length; i++)
        {
            Type importerType = importerTypes[i];
            IAssetImporter importer;
            try
            {
                importer = Activator.CreateInstance(importerType, nonPublic: true) as IAssetImporter
                    ?? throw new InvalidOperationException("Activator returned null.");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Asset importer '{importerType.FullName}' must have a parameterless constructor.",
                    exception);
            }

            if (manualAssetTypes.Contains(importer.targetAssetType))
                continue;

            if (importerTypeByAssetType.TryGetValue(importer.targetAssetType, out Type? existingType))
            {
                throw new InvalidOperationException(
                    $"Asset type '{importer.targetAssetType.FullName}' has ambiguous automatically discovered importers " +
                    $"'{existingType.FullName}' and '{importerType.FullName}'.");
            }

            importerTypeByAssetType.Add(importer.targetAssetType, importerType);
            importers.Add((importerType, importer));
        }

        lock (SYNC)
        {
            for (int i = 0; i < importers.Count; i++)
            {
                (_, IAssetImporter importer) = importers[i];
                s_loader.RegisterImporter(importer);
            }
            s_discoveredImporterTypes.Clear();
            s_discoveredImporterTypes.UnionWith(importerTypes);
        }
    }

    private static void ShutdownInternal()
    {
        if (isInitialized)
        {
            IdentityManager.ObjectUnregistered -= OnIdentityObjectUnregistered;
            s_fileSystem.ChangedBatch -= OnFileSystemChangedBatch;
            s_fileSystem.Dispose();
            s_loader.Clear();
        }

        assetRoot = string.Empty;
        artifactRoot = string.Empty;
        s_manualHoldCountById.Clear();
        s_rootHoldCountById.Clear();
        s_aggregateHoldCountById.Clear();
        s_dependencyClosureByRootId.Clear();
        s_assetRootsByOwnerId.Clear();
        s_missingAssetById.Clear();
        s_discoveredImporterTypes.Clear();
        s_manualImporterAssetTypes.Clear();
        isInitialized = false;
    }

    #endregion
}
