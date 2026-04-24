using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.IO;
using Inno.Assets.Loader;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Storage;

namespace Inno.Assets;

/// <summary>
/// Global static entry point for asset importing, caching, loading and saving.
/// </summary>
public static class AssetManager
{
    internal const string C_META_POSTFIX = ".innoasset";
    internal const string C_ARTIFACT_POSTFIX = ".abin";

    private static readonly Lock SYNC = new();
    private static readonly IdentityRegistry IDENTITY_REGISTRY = new();

    private static readonly ObjectPool<IAssetImporter> IMPORTERS = new();
    private static readonly PoolKey<int> IMPORTER_TYPE_KEY =
        IMPORTERS.DefineKey<int>("asset.importer.typeId", PoolKeyFlags.Unique);

    private static readonly ObjectPool<AssetObject> LOADED_CACHE = new();
    private static readonly PoolKey<string> CACHE_PATH_KEY =
        LOADED_CACHE.DefineKey<string>("asset.cache.path", PoolKeyFlags.Unique);
    private static readonly PoolKey<Guid> CACHE_PERSISTENT_ID_KEY =
        LOADED_CACHE.DefineKey<Guid>("asset.cache.persistentId", PoolKeyFlags.Unique);

    /// <summary>
    /// Absolute source asset root directory.
    /// </summary>
    public static string assetRoot { get; private set; } = string.Empty;

    /// <summary>
    /// Absolute imported artifact root directory.
    /// </summary>
    public static string artifactRoot { get; private set; } = string.Empty;

    private static AssetFileSystem s_fileSystem = null!;

    /// <summary>
    /// True when manager has been initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Raised when source files changed in asset root.
    /// </summary>
    public static event Action<IReadOnlyList<AssetChangedEvent>>? SourceFileSystemChanged;

    /// <summary>
    /// Initializes asset manager.
    /// </summary>
    public static void Initialize(AssetManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(options));

        lock (SYNC)
        {
            ShutdownInternal();

            assetRoot = Path.GetFullPath(options.assetRoot);
            artifactRoot = Path.GetFullPath(options.artifactRoot);
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(artifactRoot);
            s_fileSystem = new AssetFileSystem(assetRoot, autoStart: false, options.fileWatcherFlushDelayMs);
            s_fileSystem.ChangedBatch += OnFileSystemChangedBatch;

            isInitialized = true;
        }

        TypeCacheManager.Initialize();

        if (options.autoRegisterBuiltInImporters)
            RegisterBuiltInImporters();

        if (options.autoRegisterImportersFromTypeCache)
            RegisterImportersFromTypeCache();

        lock (SYNC)
        {
            ReconcileStorageState();
            if (options.enableFileSystemWatcher)
                s_fileSystem.Start();
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

    /// <summary>
    /// Registers default built-in importers.
    /// </summary>
    public static void RegisterBuiltInImporters()
    {
        RegisterImporter(new BinaryAssetImporter());
        RegisterImporter(new TextAssetImporter());
        RegisterImporter(new ShaderAssetImporter());
        RegisterImporter(new PngTextureAssetImporter());
    }

    /// <summary>
    /// Discovers and registers importer types from <c>TypeCache</c>.
    /// </summary>
    public static void RegisterImportersFromTypeCache()
    {
        foreach (Type type in TypeCache.GetTypesImplementing<IAssetImporter>())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (Activator.CreateInstance(type) is IAssetImporter importer)
                RegisterImporter(importer);
        }
    }

    /// <summary>
    /// Registers an importer by type using parameterless constructor.
    /// </summary>
    public static void RegisterImporter<TImporter>() where TImporter : IAssetImporter, new()
        => RegisterImporter(new TImporter());

    /// <summary>
    /// Registers an importer instance.
    /// </summary>
    public static void RegisterImporter(IAssetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);

        int targetRuntimeTypeId = ResolveRuntimeTypeId(importer.targetAssetType);
        lock (SYNC)
        {
            IAssetImporter? existing = IMPORTERS.First(IMPORTER_TYPE_KEY, targetRuntimeTypeId);
            if (existing is not null)
                IMPORTERS.Remove(existing);

            IMPORTERS.Add(importer).Set(IMPORTER_TYPE_KEY, targetRuntimeTypeId);
        }
    }

    /// <summary>
    /// Loads an asset from source/import cache.
    /// </summary>
    public static TAsset Load<TAsset>(string relativePath) where TAsset : AssetObject
    {
        AssetObject loaded = LoadInternal(relativePath, typeof(TAsset), forceReimport: false);
        if (loaded is not TAsset typed)
            throw new InvalidCastException($"Loaded asset is '{loaded.GetType().Name}', requested '{typeof(TAsset).Name}'.");

        return typed;
    }

    /// <summary>
    /// Forces reimport and reload for a source asset.
    /// </summary>
    public static TAsset Reimport<TAsset>(string relativePath) where TAsset : AssetObject
    {
        AssetObject loaded = LoadInternal(relativePath, typeof(TAsset), forceReimport: true);
        if (loaded is not TAsset typed)
            throw new InvalidCastException($"Reimported asset is '{loaded.GetType().Name}', requested '{typeof(TAsset).Name}'.");

        return typed;
    }

    /// <summary>
    /// Tries to load an asset.
    /// </summary>
    public static bool TryLoad<TAsset>(string relativePath, out TAsset asset) where TAsset : AssetObject
    {
        try
        {
            asset = Load<TAsset>(relativePath);
            return true;
        }
        catch
        {
            asset = null!;
            return false;
        }
    }

    /// <summary>
    /// Gets a handle for an asset path, loading it when needed.
    /// </summary>
    public static AssetRef<TAsset> GetRef<TAsset>(string relativePath) where TAsset : AssetObject
    {
        TAsset asset = Load<TAsset>(relativePath);
        return new AssetRef<TAsset>(GetIdentity(asset));
    }

    /// <summary>
    /// Tries to resolve a handle to currently loaded asset instance.
    /// </summary>
    public static bool TryResolve<TAsset>(AssetRef<TAsset> assetRef, out TAsset asset) where TAsset : AssetObject
    {
        if (!assetRef.isValid)
        {
            asset = null!;
            return false;
        }

        if (TryGetLoaded(assetRef.identity, out asset))
            return true;

        asset = null!;
        return false;
    }

    /// <summary>
    /// Tries to get already-loaded asset by path without loading from disk.
    /// </summary>
    public static bool TryGetLoaded<TAsset>(string relativePath, out TAsset asset) where TAsset : AssetObject
    {
        string normalized = NormalizeRelativePath(relativePath);
        lock (SYNC)
        {
            AssetObject? loaded = LOADED_CACHE.First(CACHE_PATH_KEY, normalized);
            if (loaded is TAsset typed)
            {
                asset = typed;
                return true;
            }
        }

        asset = null!;
        return false;
    }

    /// <summary>
    /// Tries to get already-loaded asset by identity.
    /// </summary>
    public static bool TryGetLoaded<TAsset>(in Identity identity, out TAsset asset) where TAsset : AssetObject
    {
        Guid persistentId = identity.persistentId;
        if (persistentId == Guid.Empty)
        {
            asset = null!;
            return false;
        }

        lock (SYNC)
        {
            AssetObject? loaded = LOADED_CACHE.First(CACHE_PERSISTENT_ID_KEY, persistentId);
            if (loaded is TAsset typed)
            {
                asset = typed;
                return true;
            }
        }

        int runtimeId = identity.runtimeId ?? 0;
        if (runtimeId > 0 &&
            IDENTITY_REGISTRY.TryGet(runtimeId, out IIdentityObject? runtimeObject) &&
            runtimeObject is TAsset runtimeTyped)
        {
            asset = runtimeTyped;
            return true;
        }

        asset = null!;
        return false;
    }

    /// <summary>
    /// Saves asset back to its current source path.
    /// </summary>
    public static bool Save(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.sourcePath))
            return false;

        return Save(asset.sourcePath, asset);
    }

    /// <summary>
    /// Saves asset back to source path.
    /// </summary>
    public static bool Save(string relativePath, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EnsureInitialized();

        string normalized = NormalizeRelativePath(relativePath);
        IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, asset.GetType());
        if (!importer.TryExport(asset, out byte[] sourceBytes))
            return false;

        string absSourcePath = GetAbsoluteSourcePath(normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(absSourcePath)!);
        WriteAllBytesAtomic(absSourcePath, sourceBytes);

        Unload(normalized);
        _ = LoadInternal(normalized, asset.GetType(), forceReimport: true);
        return true;
    }

    /// <summary>
    /// Unloads one asset by path.
    /// </summary>
    public static bool Unload(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        lock (SYNC)
        {
            AssetObject? loaded = LOADED_CACHE.First(CACHE_PATH_KEY, normalized);
            if (loaded is null)
                return false;

            RemoveFromCache(loaded);
            return true;
        }
    }

    /// <summary>
    /// Unloads one asset by handle.
    /// </summary>
    public static bool Unload<TAsset>(AssetRef<TAsset> assetRef) where TAsset : AssetObject
    {
        if (!assetRef.isValid)
            return false;

        lock (SYNC)
        {
            AssetObject? loaded = LOADED_CACHE.First(CACHE_PERSISTENT_ID_KEY, assetRef.identity.persistentId);
            if (loaded is null)
                return false;

            RemoveFromCache(loaded);
            return true;
        }
    }

    /// <summary>
    /// Unloads all loaded assets.
    /// </summary>
    public static void UnloadAll()
    {
        lock (SYNC)
        {
            AssetObject[] all = LOADED_CACHE.All().ToArray();
            for (int i = 0; i < all.Length; i++)
                RemoveFromCache(all[i]);
        }
    }

    /// <summary>
    /// Gets runtime artifact bytes for a loaded handle.
    /// </summary>
    public static byte[] GetArtifactBytes<TAsset>(AssetRef<TAsset> assetRef) where TAsset : AssetObject
    {
        if (!TryResolve(assetRef, out TAsset asset))
            return [];

        return asset.runtimePayload.ToArray();
    }

    /// <summary>
    /// Returns currently loaded relative paths.
    /// </summary>
    public static IReadOnlyList<string> GetLoadedPaths()
    {
        lock (SYNC)
        {
            return LOADED_CACHE.All()
                .Select(static x => x.sourcePath)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// Returns current filesystem tree graph for source assets.
    /// </summary>
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
    public static bool TryGetFileSystemEntry(string relativePath, out AssetFileEntry entry)
    {
        EnsureInitialized();
        lock (SYNC)
        {
            return s_fileSystem.TryGetEntry(relativePath, out entry);
        }
    }

    private static AssetObject LoadInternal(string relativePath, Type requestedAssetType, bool forceReimport)
    {
        EnsureInitialized();

        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Asset path is empty.");

        lock (SYNC)
        {
            if (!forceReimport)
            {
                AssetObject? loaded = LOADED_CACHE.First(CACHE_PATH_KEY, normalized);
                if (loaded is not null && requestedAssetType.IsAssignableFrom(loaded.GetType()))
                    return loaded;
            }
        }

        string absSourcePath = GetAbsoluteSourcePath(normalized);
        if (!File.Exists(absSourcePath))
            throw new FileNotFoundException($"Asset source file not found: {absSourcePath}");

        IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, requestedAssetType);
        byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
        string sourceHash = ComputeSha256Hex(sourceBytes);

        if (!forceReimport &&
            TryLoadFromDiskCache(normalized, absSourcePath, sourceHash, importer, requestedAssetType, out AssetObject? cachedAsset))
        {
            return cachedAsset;
        }

        var context = new AssetImportContext(normalized, absSourcePath, sourceBytes, sourceHash);
        AssetImportResult<AssetObject> importResult = importer.Import(context);

        AssetObject imported = importResult.asset;
        Identity importedIdentity = GetIdentity(imported);
        Guid persistentId = ReadPersistentIdFromMeta(normalized) ?? importedIdentity.persistentId;
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        imported.SetSourceInfo(normalized, sourceHash);
        imported.SetRuntimePayload(importResult.artifactBytes);

        PersistMeta(normalized, importer, imported, persistentId, importResult.dependencies);
        PersistArtifact(normalized, importResult.artifactBytes);
        CacheLoaded(normalized, imported, persistentId);
        return imported;
    }

    private static bool TryLoadFromDiskCache(
        string relativePath,
        string absSourcePath,
        string sourceHash,
        IAssetImporter importer,
        Type requestedAssetType,
        out AssetObject asset)
    {
        string metaPath = GetMetaPath(relativePath);
        string artifactPath = GetArtifactPath(relativePath);

        if (!File.Exists(metaPath) || !File.Exists(artifactPath))
        {
            asset = null!;
            return false;
        }

        AssetMeta meta;
        try
        {
            meta = DeserializeMeta(metaPath);
        }
        catch
        {
            asset = null!;
            return false;
        }
        if (!string.Equals(meta.sourceHash, sourceHash, StringComparison.Ordinal) ||
            !string.Equals(meta.importerId, importer.importerId, StringComparison.Ordinal) ||
            meta.importerVersion != importer.version)
        {
            asset = null!;
            return false;
        }

        Type? runtimeType = ResolveAssetRuntimeType(meta, importer.targetAssetType);
        if (runtimeType == null || !typeof(AssetObject).IsAssignableFrom(runtimeType))
        {
            asset = null!;
            return false;
        }

        if (!requestedAssetType.IsAssignableFrom(runtimeType))
        {
            asset = null!;
            return false;
        }

        if (!File.Exists(absSourcePath))
        {
            asset = null!;
            return false;
        }

        byte[] artifactBytes = File.ReadAllBytes(artifactPath);
        ISerializable serializable = ISerializable.CreateSerializableInstance(runtimeType);
        if (serializable is not AssetObject restored)
            throw new InvalidOperationException($"Restored instance is not AssetObject: {runtimeType.FullName}");

        if (meta.assetStateBytes.Length > 0)
        {
            SerializingState state = SerializingState.Deserialize(meta.assetStateBytes);
            ((ISerializable)restored).RestoreState(state);
        }

        restored.SetSourceInfo(relativePath, sourceHash);
        restored.SetRuntimePayload(artifactBytes);

        CacheLoaded(relativePath, restored, meta.persistentId);
        asset = restored;
        return true;
    }

    private static Type? ResolveAssetRuntimeType(AssetMeta meta, Type fallback)
    {
        if (meta.assetTypeStableId != Guid.Empty &&
            TypeCache.TryResolveType(meta.assetTypeStableId, out Type? stableResolved) &&
            stableResolved != null)
        {
            return stableResolved;
        }

        if (meta.assetRuntimeTypeId != 0 &&
            TypeCache.TryResolveType(meta.assetRuntimeTypeId, out Type? runtimeResolved) &&
            runtimeResolved != null)
        {
            return runtimeResolved;
        }

        return fallback;
    }

    private static Guid? ReadPersistentIdFromMeta(string relativePath)
    {
        string metaPath = GetMetaPath(relativePath);
        if (!File.Exists(metaPath))
            return null;

        try
        {
            AssetMeta meta = DeserializeMeta(metaPath);
            return meta.persistentId == Guid.Empty ? null : meta.persistentId;
        }
        catch
        {
            return null;
        }
    }

    private static void PersistMeta(
        string relativePath,
        IAssetImporter importer,
        AssetObject asset,
        Guid persistentId,
        IReadOnlyList<string> dependencies)
    {
        var meta = new AssetMeta
        {
            persistentId = persistentId,
            relativePath = relativePath,
            sourceHash = asset.sourceHash,
            importerId = importer.importerId,
            importerVersion = importer.version,
            dependencies = dependencies.ToArray(),
            assetStateBytes = SerializingState.Serialize(((ISerializable)asset).CaptureState())
        };

        if (TypeCache.TryGetStableTypeId(asset.GetType(), out Guid stableTypeId))
            meta.assetTypeStableId = stableTypeId;
        if (TypeCache.TryGetRuntimeTypeId(asset.GetType(), out int runtimeTypeId))
            meta.assetRuntimeTypeId = runtimeTypeId;

        byte[] bytes = SerializingState.Serialize(((ISerializable)meta).CaptureState());
        string metaPath = GetMetaPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        WriteAllBytesAtomic(metaPath, bytes);
    }

    private static AssetMeta DeserializeMeta(string metaPath)
    {
        byte[] bytes = File.ReadAllBytes(metaPath);
        SerializingState state = SerializingState.Deserialize(bytes);

        var meta = new AssetMeta();
        ((ISerializable)meta).RestoreState(state);
        return meta;
    }

    private static void PersistArtifact(string relativePath, byte[] artifactBytes)
    {
        string artifactPath = GetArtifactPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        WriteAllBytesAtomic(artifactPath, artifactBytes ?? []);
    }

    private static string GetMetaPath(string relativePath)
        => Path.Combine(assetRoot, relativePath + C_META_POSTFIX);

    private static string GetArtifactPath(string relativePath)
        => Path.Combine(artifactRoot, relativePath + C_ARTIFACT_POSTFIX);

    private static IAssetImporter ResolveImporterByPathOrAssetType(string relativePath, Type assetType)
    {
        lock (SYNC)
        {
            return ResolveImporterByPathOrAssetTypeNoLock(relativePath, assetType);
        }
    }

    private static IAssetImporter ResolveImporterByPathOrAssetTypeNoLock(string relativePath, Type assetType)
    {
        string ext = NormalizeExtension(Path.GetExtension(relativePath));
        IReadOnlyList<IAssetImporter> importers = IMPORTERS.All();
        for (int i = 0; i < importers.Count; i++)
        {
            IAssetImporter importer = importers[i];
            for (int j = 0; j < importer.supportedExtensions.Count; j++)
            {
                if (string.Equals(NormalizeExtension(importer.supportedExtensions[j]), ext, StringComparison.Ordinal))
                    return importer;
            }
        }

        int runtimeTypeId = ResolveRuntimeTypeId(assetType);
        IAssetImporter? byType = IMPORTERS.First(IMPORTER_TYPE_KEY, runtimeTypeId);
        if (byType is not null)
            return byType;

        throw new InvalidOperationException($"No importer registered for extension '{ext}' or asset type '{assetType.Name}'.");
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        string normalized = extension.Trim();
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
            normalized = "." + normalized;
        return normalized.ToLowerInvariant();
    }

    private static void CacheLoaded(string relativePath, AssetObject asset, Guid persistentId)
    {
        lock (SYNC)
        {
            AssetObject? oldByPath = LOADED_CACHE.First(CACHE_PATH_KEY, relativePath);
            if (oldByPath is not null)
                RemoveFromCache(oldByPath);

            if (persistentId != Guid.Empty)
            {
                AssetObject? oldByGuid = LOADED_CACHE.First(CACHE_PERSISTENT_ID_KEY, persistentId);
                if (oldByGuid is not null)
                    RemoveFromCache(oldByGuid);
            }

            IDENTITY_REGISTRY.Register(asset, persistentId);
            asset.SetSourceInfo(relativePath, asset.sourceHash);
            Identity identity = GetIdentity(asset);
            LOADED_CACHE.Add(asset)
                .Set(CACHE_PATH_KEY, relativePath)
                .Set(CACHE_PERSISTENT_ID_KEY, identity.persistentId);
        }
    }

    private static void RemoveFromCache(AssetObject asset)
    {
        LOADED_CACHE.Remove(asset);
        IDENTITY_REGISTRY.Unregister(asset);
    }

    private static int ResolveRuntimeTypeId(Type type)
    {
        if (TypeCache.TryGetRuntimeTypeId(type, out int runtimeTypeId))
            return runtimeTypeId;

        throw new InvalidOperationException($"Type '{type.FullName}' has no runtime type id in TypeCache.");
    }

    private static string GetAbsoluteSourcePath(string normalizedRelativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(assetRoot, normalizedRelativePath));
        string rootWithSeparator = assetRoot.EndsWith(Path.DirectorySeparatorChar)
            ? assetRoot
            : assetRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal) &&
            !string.Equals(fullPath, assetRoot, StringComparison.Ordinal))
            throw new InvalidOperationException($"Asset path escapes root: {normalizedRelativePath}");

        return fullPath;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        while (path.StartsWith("/", StringComparison.Ordinal))
            path = path[1..];
        while (path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];

        return path == "." ? string.Empty : path;
    }

    private static Identity GetIdentity(AssetObject asset)
        => ((IIdentityObject)asset).GetIdentity();

    private static void OnFileSystemChangedBatch(IReadOnlyList<AssetChangedEvent> changes)
    {
        AssetChangedEvent[] sourceChanges;
        lock (SYNC)
        {
            sourceChanges = changes
                .Where(static x => !IsInternalGeneratedPath(x.relativePath))
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
                HandleRenamedSourcePath(renamed[i].oldRelativePath, renamed[i].relativePath);

            for (int i = 0; i < deleted.Length; i++)
                HandleDeletedSourcePath(deleted[i].relativePath);

            for (int i = 0; i < createdOrChanged.Length; i++)
                HandleCreatedOrChangedSourcePath(createdOrChanged[i].relativePath);
        }

        SourceFileSystemChanged?.Invoke(sourceChanges);
    }

    private static void HandleDeletedSourcePath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (TryGetLoadedAssetByPath(path, out AssetObject? loaded))
            RemoveFromCache(loaded);

        CleanupGeneratedFiles(path);
    }

    private static void HandleCreatedOrChangedSourcePath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        string absSourcePath = GetAbsoluteSourcePath(path);
        if (!File.Exists(absSourcePath))
            return;

        if (TryGetLoadedAssetByPath(path, out AssetObject? loaded))
        {
            Type loadedType = loaded.GetType();
            RemoveFromCache(loaded);
            try
            {
                _ = LoadInternal(path, loadedType, forceReimport: true);
            }
            catch
            {
                // Keep runtime alive; caller can inspect filesystem event and decide recovery path.
            }
            return;
        }

        _ = TryImportSourceToDisk(path);
    }

    private static bool TryGetLoadedAssetByPath(string normalizedPath, out AssetObject loaded)
    {
        AssetObject? cached = LOADED_CACHE.First(CACHE_PATH_KEY, normalizedPath);
        if (cached is null)
        {
            loaded = null!;
            return false;
        }

        loaded = cached;
        return true;
    }

    private static void HandleRenamedSourcePath(string oldRelativePath, string newRelativePath)
    {
        string oldPath = NormalizeRelativePath(oldRelativePath);
        string newPath = NormalizeRelativePath(newRelativePath);
        if (string.IsNullOrWhiteSpace(newPath) || string.IsNullOrWhiteSpace(oldPath))
            return;

        bool isDirectoryRename = Directory.Exists(GetAbsoluteSourcePath(newPath));
        if (isDirectoryRename)
        {
            HandleRenamedDirectory(oldPath, newPath);
            return;
        }

        Type? reloadType = null;

        AssetObject? oldCached = LOADED_CACHE.First(CACHE_PATH_KEY, oldPath);
        if (oldCached is not null)
        {
            reloadType = oldCached.GetType();
            RemoveFromCache(oldCached);
        }

        AssetObject? newCached = LOADED_CACHE.First(CACHE_PATH_KEY, newPath);
        if (newCached is not null)
        {
            reloadType ??= newCached.GetType();
            RemoveFromCache(newCached);
        }

        MoveGeneratedFiles(oldPath, newPath);

        string absNewSourcePath = GetAbsoluteSourcePath(newPath);
        if (!File.Exists(absNewSourcePath))
        {
            CleanupGeneratedFiles(newPath);
            return;
        }

        try
        {
            if (reloadType is not null)
            {
                _ = LoadInternal(newPath, reloadType, forceReimport: true);
            }
            else
            {
                _ = TryImportSourceToDisk(newPath);
            }
        }
        catch
        {
            // Watcher callbacks should not crash the runtime.
        }
    }

    private static void HandleRenamedDirectory(string oldRelativePath, string newRelativePath)
    {
        AssetObject[] affected = LOADED_CACHE.All()
            .Where(x => IsSameOrUnderPath(x.sourcePath, oldRelativePath))
            .ToArray();

        for (int i = 0; i < affected.Length; i++)
        {
            AssetObject loaded = affected[i];
            Guid persistentId = GetIdentity(loaded).persistentId;
            string remapped = RemapPathPrefix(loaded.sourcePath, oldRelativePath, newRelativePath);
            RemoveFromCache(loaded);
            IDENTITY_REGISTRY.Register(loaded, persistentId);
            loaded.SetSourceInfo(remapped, loaded.sourceHash);
            LOADED_CACHE.Add(loaded)
                .Set(CACHE_PATH_KEY, remapped)
                .Set(CACHE_PERSISTENT_ID_KEY, GetIdentity(loaded).persistentId);
        }

        MoveGeneratedTree(oldRelativePath, newRelativePath);
    }

    private static bool IsSameOrUnderPath(string path, string parentPath)
        => string.Equals(path, parentPath, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(parentPath + "/", StringComparison.OrdinalIgnoreCase);

    private static string RemapPathPrefix(string path, string oldPrefix, string newPrefix)
    {
        if (string.Equals(path, oldPrefix, StringComparison.OrdinalIgnoreCase))
            return newPrefix;

        return newPrefix + path[oldPrefix.Length..];
    }

    private static bool IsInternalGeneratedPath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (path.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Contains(C_META_POSTFIX + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryImportSourceToDisk(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized) || IsInternalGeneratedPath(normalized))
            return false;

        string absSourcePath = GetAbsoluteSourcePath(normalized);
        if (!File.Exists(absSourcePath))
            return false;

        IAssetImporter importer;
        try
        {
            importer = ResolveImporterByPathOrAssetTypeNoLock(normalized, typeof(AssetObject));
        }
        catch
        {
            return false;
        }

        byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
        string sourceHash = ComputeSha256Hex(sourceBytes);
        var context = new AssetImportContext(normalized, absSourcePath, sourceBytes, sourceHash);
        AssetImportResult<AssetObject> importResult = importer.Import(context);

        AssetObject imported = importResult.asset;
        Guid persistentId = ReadPersistentIdFromMeta(normalized) ?? GetIdentity(imported).persistentId;
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        imported.SetSourceInfo(normalized, sourceHash);
        imported.SetRuntimePayload(importResult.artifactBytes);
        PersistMeta(normalized, importer, imported, persistentId, importResult.dependencies);
        PersistArtifact(normalized, importResult.artifactBytes);
        return true;
    }

    private static void CleanupGeneratedFiles(string relativePath)
    {
        TryDeleteFile(GetMetaPath(relativePath));
        TryDeleteFile(GetArtifactPath(relativePath));
    }

    private static void MoveGeneratedFiles(string oldRelativePath, string newRelativePath)
    {
        MoveGeneratedFile(GetMetaPath(oldRelativePath), GetMetaPath(newRelativePath));
        MoveGeneratedFile(GetArtifactPath(oldRelativePath), GetArtifactPath(newRelativePath));
    }

    private static void MoveGeneratedTree(string oldRelativePath, string newRelativePath)
    {
        string oldMetaDir = Path.Combine(assetRoot, oldRelativePath);
        string newMetaDir = Path.Combine(assetRoot, newRelativePath);
        string oldArtifactDir = Path.Combine(artifactRoot, oldRelativePath);
        string newArtifactDir = Path.Combine(artifactRoot, newRelativePath);

        if (Directory.Exists(oldMetaDir))
            MoveGeneratedTreeFiles(oldMetaDir, newMetaDir, C_META_POSTFIX);
        if (Directory.Exists(oldArtifactDir))
            MoveGeneratedTreeFiles(oldArtifactDir, newArtifactDir, C_ARTIFACT_POSTFIX);
    }

    private static void MoveGeneratedTreeFiles(string oldRootDir, string newRootDir, string postfix)
    {
        string[] files = Directory.GetFiles(oldRootDir, "*" + postfix, SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string oldFile = files[i];
            string relative = Path.GetRelativePath(oldRootDir, oldFile);
            string newFile = Path.Combine(newRootDir, relative);
            MoveGeneratedFile(oldFile, newFile);
        }

        TryDeleteEmptyDirectory(oldRootDir);
    }

    private static void MoveGeneratedFile(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;

        if (!File.Exists(oldPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(oldPath, newPath);
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
            return;

        File.Delete(path);
        TryDeleteEmptyDirectory(Path.GetDirectoryName(path)!);
    }

    private static void TryDeleteEmptyDirectory(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Directory.Exists(absolutePath))
            return;

        string normalized = Path.GetFullPath(absolutePath);
        if (string.Equals(normalized, assetRoot, StringComparison.Ordinal) ||
            string.Equals(normalized, artifactRoot, StringComparison.Ordinal))
            return;

        if (Directory.EnumerateFileSystemEntries(absolutePath).Any())
            return;

        Directory.Delete(absolutePath);
    }

    private static void ReconcileStorageState()
    {
        string[] sourceFiles = Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Where(static x => !x.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (int i = 0; i < sourceFiles.Length; i++)
        {
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(assetRoot, sourceFiles[i]));
            ReconcileSourceFile(relativePath);
        }

        CleanupOrphanMetadataAndArtifacts();
        s_fileSystem.Refresh();
    }

    private static void ReconcileSourceFile(string relativePath)
    {
        string absSourcePath = GetAbsoluteSourcePath(relativePath);
        if (!File.Exists(absSourcePath))
            return;

        IAssetImporter importer;
        try
        {
            importer = ResolveImporterByPathOrAssetTypeNoLock(relativePath, typeof(AssetObject));
        }
        catch
        {
            return;
        }

        string metaPath = GetMetaPath(relativePath);
        string artifactPath = GetArtifactPath(relativePath);
        byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
        string sourceHash = ComputeSha256Hex(sourceBytes);
        bool requiresReimport = !File.Exists(metaPath) || !File.Exists(artifactPath);

        if (!requiresReimport)
        {
            try
            {
                AssetMeta meta = DeserializeMeta(metaPath);
                requiresReimport =
                    !string.Equals(meta.sourceHash, sourceHash, StringComparison.Ordinal) ||
                    !string.Equals(meta.importerId, importer.importerId, StringComparison.Ordinal) ||
                    meta.importerVersion != importer.version;

                if (!requiresReimport)
                {
                    _ = File.ReadAllBytes(artifactPath);
                    if (meta.assetStateBytes.Length > 0)
                        _ = SerializingState.Deserialize(meta.assetStateBytes);
                }
            }
            catch
            {
                requiresReimport = true;
            }
        }

        if (requiresReimport)
            _ = TryImportSourceToDisk(relativePath);
    }

    private static void CleanupOrphanMetadataAndArtifacts()
    {
        string[] allMetaFiles = Directory.GetFiles(assetRoot, "*" + C_META_POSTFIX, SearchOption.AllDirectories);
        for (int i = 0; i < allMetaFiles.Length; i++)
        {
            string relativeMeta = NormalizeRelativePath(Path.GetRelativePath(assetRoot, allMetaFiles[i]));
            string relativeSource = relativeMeta[..^C_META_POSTFIX.Length];
            string sourcePath = GetAbsoluteSourcePath(relativeSource);
            if (File.Exists(sourcePath))
                continue;

            CleanupGeneratedFiles(relativeSource);
        }

        string[] allArtifactFiles = Directory.GetFiles(artifactRoot, "*" + C_ARTIFACT_POSTFIX, SearchOption.AllDirectories);
        for (int i = 0; i < allArtifactFiles.Length; i++)
        {
            string relativeArtifact = NormalizeRelativePath(Path.GetRelativePath(artifactRoot, allArtifactFiles[i]));
            string relativeSource = relativeArtifact[..^C_ARTIFACT_POSTFIX.Length];
            string sourcePath = GetAbsoluteSourcePath(relativeSource);
            string metaPath = GetMetaPath(relativeSource);
            if (File.Exists(sourcePath) && File.Exists(metaPath))
                continue;

            TryDeleteFile(allArtifactFiles[i]);
        }
    }

    private static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes ?? []);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void EnsureInitialized()
    {
        if (!isInitialized)
            throw new InvalidOperationException("AssetManager is not initialized.");
    }

    private static void ShutdownInternal()
    {
        AssetObject[] loaded = LOADED_CACHE.All().ToArray();
        for (int i = 0; i < loaded.Length; i++)
            IDENTITY_REGISTRY.Unregister(loaded[i]);

        LOADED_CACHE.RemoveAll();
        IMPORTERS.RemoveAll();

        if (isInitialized)
        {
            s_fileSystem.ChangedBatch -= OnFileSystemChangedBatch;
            s_fileSystem.Dispose();
        }

        assetRoot = string.Empty;
        artifactRoot = string.Empty;
        isInitialized = false;
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> sourceBytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(sourceBytes, hash);
        return Convert.ToHexString(hash);
    }
}
