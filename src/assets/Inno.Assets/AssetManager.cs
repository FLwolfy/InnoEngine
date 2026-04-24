using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.IO;
using Inno.Assets.Loader;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Global static entry point for asset importing, caching, loading and saving.
/// </summary>
public static class AssetManager
{
    private const string C_META_POSTFIX = ".innoasset";
    private const string C_ARTIFACT_POSTFIX = ".abin";

    private static readonly Lock SYNC = new();
    private static readonly IdentityRegistry IDENTITY_REGISTRY = new();

    private static readonly Dictionary<string, IAssetImporter> IMPORTER_BY_EXTENSION = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, IAssetImporter> IMPORTER_BY_ASSET_TYPE = new();
    private static readonly Dictionary<string, AssetCacheEntry> CACHE_BY_PATH = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, AssetCacheEntry> CACHE_BY_GUID = new();

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
    /// Initializes asset manager with minimal options.
    /// </summary>
    /// <param name="assetRoot">Source asset root directory.</param>
    /// <param name="artifactRoot">Imported artifact root directory.</param>
    /// <param name="registerBuiltInImporters">Whether to register built-in importers.</param>
    public static void Initialize(string assetRoot, string artifactRoot, bool registerBuiltInImporters = true)
    {
        Initialize(new AssetManagerOptions
        {
            assetRoot = assetRoot,
            artifactRoot = artifactRoot,
            autoRegisterBuiltInImporters = registerBuiltInImporters,
            autoRegisterImportersFromTypeCache = false
        });
    }

    /// <summary>
    /// Initializes asset manager.
    /// </summary>
    /// <param name="options">Initialization options.</param>
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

            isInitialized = true;
        }

        TypeCacheManager.Initialize();

        if (options.autoRegisterBuiltInImporters)
            RegisterBuiltInImporters();

        if (options.autoRegisterImportersFromTypeCache)
            RegisterImportersFromTypeCache();
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
    /// <typeparam name="TImporter">Importer type.</typeparam>
    public static void RegisterImporter<TImporter>() where TImporter : IAssetImporter, new()
        => RegisterImporter(new TImporter());

    /// <summary>
    /// Registers an importer instance.
    /// </summary>
    /// <param name="importer">Importer to register.</param>
    public static void RegisterImporter(IAssetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);

        lock (SYNC)
        {
            IMPORTER_BY_ASSET_TYPE[importer.targetAssetType] = importer;

            for (int i = 0; i < importer.supportedExtensions.Count; i++)
            {
                string ext = NormalizeExtension(importer.supportedExtensions[i]);
                IMPORTER_BY_EXTENSION[ext] = importer;
            }
        }
    }

    /// <summary>
    /// Loads an asset from source/import cache.
    /// </summary>
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <returns>Loaded asset instance.</returns>
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
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <returns>Reimported asset instance.</returns>
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
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <param name="asset">Loaded asset when successful.</param>
    /// <returns>True on success.</returns>
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
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <returns>Asset handle.</returns>
    public static AssetHandle<TAsset> GetHandle<TAsset>(string relativePath) where TAsset : AssetObject
    {
        TAsset asset = Load<TAsset>(relativePath);
        return new AssetHandle<TAsset>(asset.persistentId, asset.runtimeId ?? 0);
    }

    /// <summary>
    /// Tries to resolve a handle to currently loaded asset instance.
    /// </summary>
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="handle">Asset handle.</param>
    /// <param name="asset">Resolved asset when successful.</param>
    /// <returns>True when handle resolves to loaded instance.</returns>
    public static bool TryResolve<TAsset>(AssetHandle<TAsset> handle, out TAsset asset) where TAsset : AssetObject
    {
        if (!handle.isValid)
        {
            asset = null!;
            return false;
        }

        lock (SYNC)
        {
            if (CACHE_BY_GUID.TryGetValue(handle.persistentId, out AssetCacheEntry? byGuid) &&
                byGuid.asset is TAsset typedByGuid)
            {
                asset = typedByGuid;
                return true;
            }
        }

        if (handle.runtimeId != 0 &&
            IDENTITY_REGISTRY.TryGet(handle.runtimeId, out IIdentityObject? runtimeObj) &&
            runtimeObj is TAsset typedByRuntime)
        {
            asset = typedByRuntime;
            return true;
        }

        asset = null!;
        return false;
    }

    /// <summary>
    /// Tries to get already-loaded asset by path without loading from disk.
    /// </summary>
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <param name="asset">Loaded asset when found.</param>
    /// <returns>True when asset is already loaded.</returns>
    public static bool TryGetLoaded<TAsset>(string relativePath, out TAsset asset) where TAsset : AssetObject
    {
        string normalized = AssetPath.Normalize(relativePath);
        lock (SYNC)
        {
            if (CACHE_BY_PATH.TryGetValue(normalized, out AssetCacheEntry? entry) &&
                entry.asset is TAsset typed)
            {
                asset = typed;
                return true;
            }
        }

        asset = null!;
        return false;
    }

    /// <summary>
    /// Saves asset back to its current source path.
    /// </summary>
    /// <param name="asset">Asset to save.</param>
    /// <returns>True when save succeeded.</returns>
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
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <param name="asset">Asset to save.</param>
    /// <returns>True when save succeeded.</returns>
    public static bool Save(string relativePath, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EnsureInitialized();

        string normalized = AssetPath.Normalize(relativePath);
        IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, asset.GetType());
        if (!importer.TryExport(asset, out byte[] sourceBytes))
            return false;

        string absSourcePath = Path.Combine(assetRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(absSourcePath)!);
        File.WriteAllBytes(absSourcePath, sourceBytes);

        Unload(normalized);
        _ = LoadInternal(normalized, asset.GetType(), forceReimport: true);
        return true;
    }

    /// <summary>
    /// Unloads one asset by path.
    /// </summary>
    /// <param name="relativePath">Path relative to <see cref="assetRoot"/>.</param>
    /// <returns>True when an asset was unloaded.</returns>
    public static bool Unload(string relativePath)
    {
        string normalized = AssetPath.Normalize(relativePath);
        lock (SYNC)
        {
            if (!CACHE_BY_PATH.TryGetValue(normalized, out AssetCacheEntry? entry))
                return false;

            RemoveFromCache(entry);
            return true;
        }
    }

    /// <summary>
    /// Unloads one asset by handle.
    /// </summary>
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="handle">Asset handle.</param>
    /// <returns>True when an asset was unloaded.</returns>
    public static bool Unload<TAsset>(AssetHandle<TAsset> handle) where TAsset : AssetObject
    {
        if (!handle.isValid)
            return false;

        lock (SYNC)
        {
            if (!CACHE_BY_GUID.TryGetValue(handle.persistentId, out AssetCacheEntry? entry))
                return false;

            RemoveFromCache(entry);
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
            foreach (AssetCacheEntry entry in CACHE_BY_PATH.Values.ToArray())
                RemoveFromCache(entry);
        }
    }

    /// <summary>
    /// Gets runtime artifact bytes for a loaded handle.
    /// </summary>
    /// <typeparam name="TAsset">Asset type.</typeparam>
    /// <param name="handle">Asset handle.</param>
    /// <returns>Artifact bytes, or empty array when unresolved.</returns>
    public static byte[] GetArtifactBytes<TAsset>(AssetHandle<TAsset> handle) where TAsset : AssetObject
    {
        if (!TryResolve(handle, out TAsset asset))
            return [];

        return asset.runtimePayload.ToArray();
    }

    /// <summary>
    /// Returns currently loaded relative paths.
    /// </summary>
    /// <returns>Snapshot of loaded paths.</returns>
    public static IReadOnlyList<string> GetLoadedPaths()
    {
        lock (SYNC)
        {
            return CACHE_BY_PATH.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private static AssetObject LoadInternal(string relativePath, Type requestedAssetType, bool forceReimport)
    {
        EnsureInitialized();

        string normalized = AssetPath.Normalize(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Asset path is empty.");

        lock (SYNC)
        {
            if (!forceReimport &&
                CACHE_BY_PATH.TryGetValue(normalized, out AssetCacheEntry? cached) &&
                requestedAssetType.IsAssignableFrom(cached.asset.GetType()))
            {
                return cached.asset;
            }
        }

        string absSourcePath = Path.Combine(assetRoot, normalized);
        if (!File.Exists(absSourcePath))
            throw new FileNotFoundException($"Asset source file not found: {absSourcePath}");

        IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, requestedAssetType);
        string sourceHash;
        byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
        sourceHash = AssetHashUtility.ComputeSha256Hex(sourceBytes);

        if (!forceReimport &&
            TryLoadFromDiskCache(normalized, absSourcePath, sourceHash, importer, requestedAssetType, out AssetObject? cachedAsset))
        {
            return cachedAsset;
        }

        var context = new AssetImportContext(normalized, absSourcePath, sourceBytes, sourceHash);
        AssetImportResult importResult = importer.Import(context);

        AssetObject imported = importResult.asset;
        Guid persistentId = ReadPersistentIdFromMeta(normalized) ?? imported.persistentId;
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();

        imported.SetPersistentId(persistentId);
        imported.SetSourceInfo(normalized, sourceHash);
        imported.SetRuntimePayload(importResult.artifactBytes);

        PersistMeta(normalized, importer, imported, importResult.dependencies);
        PersistArtifact(normalized, importResult.artifactBytes);

        CacheLoaded(normalized, importer.importerId, imported);
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

        AssetMeta meta = DeserializeMeta(metaPath);
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

        restored.SetPersistentId(meta.persistentId);
        restored.SetSourceInfo(relativePath, sourceHash);
        restored.SetRuntimePayload(artifactBytes);

        CacheLoaded(relativePath, importer.importerId, restored);
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

    private static void PersistMeta(string relativePath, IAssetImporter importer, AssetObject asset, IReadOnlyList<string> dependencies)
    {
        var meta = new AssetMeta
        {
            persistentId = asset.persistentId,
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
        File.WriteAllBytes(metaPath, bytes);
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
        File.WriteAllBytes(artifactPath, artifactBytes ?? []);
    }

    private static string GetMetaPath(string relativePath)
        => Path.Combine(assetRoot, relativePath + C_META_POSTFIX);

    private static string GetArtifactPath(string relativePath)
        => Path.Combine(artifactRoot, relativePath + C_ARTIFACT_POSTFIX);

    private static IAssetImporter ResolveImporterByPathOrAssetType(string relativePath, Type assetType)
    {
        string ext = NormalizeExtension(Path.GetExtension(relativePath));
        lock (SYNC)
        {
            if (IMPORTER_BY_EXTENSION.TryGetValue(ext, out IAssetImporter? byExt))
                return byExt;

            if (IMPORTER_BY_ASSET_TYPE.TryGetValue(assetType, out IAssetImporter? byType))
                return byType;
        }

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

    private static void CacheLoaded(string relativePath, string importerId, AssetObject asset)
    {
        lock (SYNC)
        {
            if (CACHE_BY_PATH.TryGetValue(relativePath, out AssetCacheEntry? oldByPath))
                RemoveFromCache(oldByPath);

            if (CACHE_BY_GUID.TryGetValue(asset.persistentId, out AssetCacheEntry? oldByGuid))
                RemoveFromCache(oldByGuid);

            IDENTITY_REGISTRY.Register(asset);

            var entry = new AssetCacheEntry(relativePath, importerId, asset);
            CACHE_BY_PATH[relativePath] = entry;
            CACHE_BY_GUID[asset.persistentId] = entry;
        }
    }

    private static void RemoveFromCache(AssetCacheEntry entry)
    {
        CACHE_BY_PATH.Remove(entry.relativePath);
        CACHE_BY_GUID.Remove(entry.asset.persistentId);
        IDENTITY_REGISTRY.Unregister(entry.asset);
    }

    private static void EnsureInitialized()
    {
        if (!isInitialized)
            throw new InvalidOperationException("AssetManager is not initialized.");
    }

    private static void ShutdownInternal()
    {
        foreach (AssetCacheEntry entry in CACHE_BY_PATH.Values.ToArray())
            IDENTITY_REGISTRY.Unregister(entry.asset);

        IMPORTER_BY_EXTENSION.Clear();
        IMPORTER_BY_ASSET_TYPE.Clear();
        CACHE_BY_PATH.Clear();
        CACHE_BY_GUID.Clear();

        assetRoot = string.Empty;
        artifactRoot = string.Empty;
        isInitialized = false;
    }

    private sealed class AssetCacheEntry(string relativePath, string importerId, AssetObject asset)
    {
        public string relativePath { get; } = relativePath;
        public string importerId { get; } = importerId;
        public AssetObject asset { get; } = asset;
    }

    private sealed class AssetMeta : ISerializable
    {
        [SerializableProperty] public Guid persistentId { get; set; }
        [SerializableProperty] public string relativePath { get; set; } = string.Empty;
        [SerializableProperty] public string sourceHash { get; set; } = string.Empty;
        [SerializableProperty] public string importerId { get; set; } = string.Empty;
        [SerializableProperty] public int importerVersion { get; set; }
        [SerializableProperty] public Guid assetTypeStableId { get; set; }
        [SerializableProperty] public int assetRuntimeTypeId { get; set; }
        [SerializableProperty] public byte[] assetStateBytes { get; set; } = [];
        [SerializableProperty] public string[] dependencies { get; set; } = [];
    }
}
