using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

using Inno.Assets.Core;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Storage;

namespace Inno.Assets.Loader;

/// <summary>
/// Imports, caches, loads, saves and unloads assets for one asset/artifact root pair.
/// </summary>
public sealed class AssetLoader
{
    internal const string C_META_POSTFIX = ".imeta";
    internal const string C_ARTIFACT_POSTFIX = ".abin";

    private readonly Lock m_sync = new();

    private readonly ObjectPool<IAssetImporter> m_importers = new();
    private readonly PoolKey<int> m_importerTypeKey;

    private readonly ObjectPool<AssetObject> m_loadedCache = new();
    private readonly PoolKey<string> m_cachePathKey;
    private readonly PoolKey<Guid> m_cachePersistentIdKey;

    /// <summary>
    /// Absolute source asset root directory.
    /// </summary>
    public string assetRoot { get; }

    /// <summary>
    /// Absolute imported artifact root directory.
    /// </summary>
    public string artifactRoot { get; }

    /// <summary>
    /// Creates an asset loader.
    /// </summary>
    public AssetLoader(string assetRoot, string artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(assetRoot));
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("Artifact root is required.", nameof(artifactRoot));

        this.assetRoot = Path.GetFullPath(assetRoot);
        this.artifactRoot = Path.GetFullPath(artifactRoot);
        Directory.CreateDirectory(this.assetRoot);
        Directory.CreateDirectory(this.artifactRoot);

        m_importerTypeKey = m_importers.DefineKey<int>("asset.importer.typeId", PoolKeyFlags.Unique);
        m_cachePathKey = m_loadedCache.DefineKey<string>("asset.cache.path", PoolKeyFlags.Unique);
        m_cachePersistentIdKey = m_loadedCache.DefineKey<Guid>("asset.cache.persistentId", PoolKeyFlags.Unique);
    }

    #region Importer Registry

    /// <summary>
    /// Registers an importer by type using parameterless constructor.
    /// </summary>
    public void RegisterImporter<TImporter>() where TImporter : IAssetImporter, new()
        => RegisterImporter(new TImporter());

    /// <summary>
    /// Registers an importer instance.
    /// </summary>
    public void RegisterImporter(IAssetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);

        int targetRuntimeTypeId = ResolveRuntimeTypeId(importer.targetAssetType);
        lock (m_sync)
        {
            IAssetImporter? existing = m_importers.First(m_importerTypeKey, targetRuntimeTypeId);
            if (existing is not null)
                m_importers.Remove(existing);

            m_importers.Add(importer).Set(m_importerTypeKey, targetRuntimeTypeId);
        }
    }

    #endregion

    #region Importing

    /// <summary>
    /// Imports one source asset to metadata and artifact files without loading it into memory.
    /// </summary>
    public bool Import(string relativePath)
    {
        return TryImportSourceToDisk(relativePath);
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads an asset from existing metadata and artifact files into memory.
    /// </summary>
    public bool Load(string relativePath, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);

        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return TryLoadFromDiskCache(normalized, requestedAssetType, out _);
    }

    /// <summary>
    /// Resolves an identity to a currently loaded asset instance.
    /// </summary>
    public AssetObject? Resolve(Identity identity, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);

        if (identity.persistentId == Guid.Empty)
            return null;

        if (TryLoadFromMemoryCache(identity, requestedAssetType, out AssetObject? loaded))
            return loaded;

        return null;
    }

    /// <summary>
    /// Gets an identity for a path without loading the asset.
    /// </summary>
    public Identity GetIdentity(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            AssetObject? loaded = m_loadedCache.First(m_cachePathKey, normalized);
            if (loaded is not null)
                return GetIdentity(loaded);
        }

        Guid? persistentId = ReadPersistentIdFromMeta(normalized);
        return persistentId is Guid id ? new Identity(id) : default;
    }

    /// <summary>
    /// Returns currently loaded relative paths.
    /// </summary>
    public IReadOnlyList<string> GetLoadedPaths()
    {
        lock (m_sync)
        {
            return m_loadedCache.All()
                .Select(static x => x.sourcePath)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    #endregion

    #region Saving

    /// <summary>
    /// Saves asset back to its current source path.
    /// </summary>
    public bool Save(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.sourcePath))
            return false;

        return Save(asset.sourcePath, asset);
    }

    /// <summary>
    /// Saves asset back to source path.
    /// </summary>
    public bool Save(string relativePath, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        string normalized = NormalizeRelativePath(relativePath);
        IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, asset.GetType());
        if (!importer.TryExport(asset, out byte[] sourceBytes))
            return false;

        string absSourcePath = GetAbsoluteSourcePath(normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(absSourcePath)!);
        WriteAllBytesAtomic(absSourcePath, sourceBytes);

        Unload(normalized);
        if (!TryImportSourceToDisk(normalized))
            return false;

        return LoadByType(normalized, asset.GetType());
    }

    #endregion

    #region Unloading

    /// <summary>
    /// Unloads one asset by path.
    /// </summary>
    public bool Unload(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            AssetObject? loaded = m_loadedCache.First(m_cachePathKey, normalized);
            if (loaded is null)
                return false;

            RemoveFromCache(loaded);
            return true;
        }
    }

    /// <summary>
    /// Unloads one asset by identity.
    /// </summary>
    public bool Unload(Identity identity)
    {
        if (identity.persistentId == Guid.Empty)
            return false;

        lock (m_sync)
        {
            AssetObject? loaded = m_loadedCache.First(m_cachePersistentIdKey, identity.persistentId);
            if (loaded is null)
                return false;

            RemoveFromCache(loaded);
            return true;
        }
    }

    /// <summary>
    /// Unloads all loaded assets.
    /// </summary>
    public void UnloadAll()
    {
        lock (m_sync)
        {
            AssetObject[] all = m_loadedCache.All().ToArray();
            for (int i = 0; i < all.Length; i++)
                RemoveFromCache(all[i]);
        }
    }

    /// <summary>
    /// Clears loaded assets and registered importers.
    /// </summary>
    public void Clear()
    {
        lock (m_sync)
        {
            AssetObject[] loaded = m_loadedCache.All().ToArray();
            for (int i = 0; i < loaded.Length; i++)
                IdentityManager.Unregister(loaded[i]);

            m_loadedCache.RemoveAll();
            m_importers.RemoveAll();
        }
    }

    #endregion

    #region Storage Sync

    /// <summary>
    /// Imports stale sources and removes generated files whose sources no longer exist.
    /// </summary>
    public void ReconcileStorageState()
    {
        string[] sourceFiles = Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Where(static x => !x.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sourceFiles.Length; i++)
        {
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(assetRoot, sourceFiles[i]));
            sourcePaths.Add(relativePath);
            ReconcileSourceFile(relativePath);
        }

        CleanupOrphanMetadataAndArtifacts();
        ReconcileLoadedCache(sourcePaths);
    }

    /// <summary>
    /// Returns true when the relative path points to asset-generated metadata.
    /// </summary>
    public bool IsInternalGeneratedPath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (path.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Contains(C_META_POSTFIX + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Handles a deleted source path by unloading it and removing generated files.
    /// </summary>
    public void HandleDeletedSourcePath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        lock (m_sync)
        {
            if (TryGetLoadedAssetByPathNoLock(path, out AssetObject? loaded))
                RemoveFromCache(loaded);
        }

        CleanupGeneratedFiles(path);
    }

    /// <summary>
    /// Handles a created or changed source path by importing or reloading it.
    /// </summary>
    public void HandleCreatedOrChangedSourcePath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        string absSourcePath = GetAbsoluteSourcePath(path);
        if (!File.Exists(absSourcePath))
            return;

        AssetObject? loaded = null;
        lock (m_sync)
        {
            if (TryGetLoadedAssetByPathNoLock(path, out AssetObject? cached))
            {
                loaded = cached;
                RemoveFromCache(cached);
            }
        }

        if (loaded is not null)
        {
            try
            {
                _ = TryImportSourceToDisk(path) && LoadByType(path, loaded.GetType());
            }
            catch
            {
                // Watcher callbacks should not crash the runtime.
            }
            return;
        }

        _ = TryImportSourceToDisk(path);
    }

    /// <summary>
    /// Handles a renamed source file or directory path.
    /// </summary>
    public void HandleRenamedSourcePath(string oldRelativePath, string newRelativePath)
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
        lock (m_sync)
        {
            AssetObject? oldCached = m_loadedCache.First(m_cachePathKey, oldPath);
            if (oldCached is not null)
            {
                reloadType = oldCached.GetType();
                RemoveFromCache(oldCached);
            }

            AssetObject? newCached = m_loadedCache.First(m_cachePathKey, newPath);
            if (newCached is not null)
            {
                reloadType ??= newCached.GetType();
                RemoveFromCache(newCached);
            }
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
                _ = TryImportSourceToDisk(newPath) && LoadByType(newPath, reloadType);
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

    #endregion

    #region Load Internals

    private bool LoadByType(string relativePath, Type requestedAssetType)
    {
        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return TryLoadFromDiskCache(normalized, requestedAssetType, out _);
    }

    private bool TryLoadFromMemoryCache(in Identity identity, Type requestedAssetType, out AssetObject? asset)
    {
        Guid persistentId = identity.persistentId;
        if (persistentId == Guid.Empty)
        {
            asset = null;
            return false;
        }

        lock (m_sync)
        {
            AssetObject? loaded = m_loadedCache.First(m_cachePersistentIdKey, persistentId);
            if (loaded is not null && requestedAssetType.IsAssignableFrom(loaded.GetType()))
            {
                asset = loaded;
                return true;
            }
        }

        int runtimeId = identity.runtimeId ?? 0;
            if (runtimeId > 0 &&
                IdentityManager.Get<AssetObject>(runtimeId) is AssetObject runtimeAsset &&
            requestedAssetType.IsAssignableFrom(runtimeAsset.GetType()))
            {
                asset = runtimeAsset;
                return true;
            }

        asset = null;
        return false;
    }

    private bool TryLoadFromDiskCache(string relativePath, Type requestedAssetType, out AssetObject? asset)
    {
        try
        {
            string absSourcePath = GetAbsoluteSourcePath(relativePath);
            if (!File.Exists(absSourcePath))
            {
                asset = null;
                return false;
            }

            IAssetImporter importer = ResolveImporterByPathOrAssetType(relativePath, requestedAssetType);
            byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
            string sourceHash = ComputeSha256Hex(sourceBytes);
            string metaPath = GetMetaPath(relativePath);
            string artifactPath = GetArtifactPath(relativePath);

            if (!File.Exists(metaPath) || !File.Exists(artifactPath))
            {
                asset = null;
                return false;
            }

            AssetMeta meta = DeserializeMeta(metaPath);
            if (!string.Equals(meta.sourceHash, sourceHash, StringComparison.Ordinal) ||
                !string.Equals(meta.importerId, importer.importerId, StringComparison.Ordinal) ||
                meta.importerVersion != importer.version)
            {
                asset = null;
                return false;
            }

            Type? runtimeType = ResolveAssetRuntimeType(meta, importer.targetAssetType);
            if (runtimeType == null ||
                !typeof(AssetObject).IsAssignableFrom(runtimeType) ||
                !requestedAssetType.IsAssignableFrom(runtimeType))
            {
                asset = null;
                return false;
            }

            byte[] artifactBytes = File.ReadAllBytes(artifactPath);
            ISerializable serializable = ISerializable.CreateSerializableInstance(runtimeType);
            if (serializable is not AssetObject restored)
            {
                asset = null;
                return false;
            }

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
        catch
        {
            asset = null;
            return false;
        }
    }

    private bool TryImportSourceToDisk(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized) || IsInternalGeneratedPath(normalized))
            return false;

        return TryImportFromDiskRaw(normalized);
    }

    private bool TryImportFromDiskRaw(string relativePath)
    {
        try
        {
            string absSourcePath = GetAbsoluteSourcePath(relativePath);
            if (!File.Exists(absSourcePath))
                return false;

            IAssetImporter importer = ResolveImporterByPathOrAssetType(relativePath, typeof(AssetObject));

            byte[] sourceBytes = File.ReadAllBytes(absSourcePath);
            string sourceHash = ComputeSha256Hex(sourceBytes);
            var context = new AssetImportContext(relativePath, absSourcePath, sourceBytes, sourceHash);
            AssetImportResult<AssetObject> importResult = importer.Import(context);

            AssetObject imported = importResult.asset;
            Identity importedIdentity = GetIdentity(imported);
            Guid persistentId = ReadPersistentIdFromMeta(relativePath) ?? importedIdentity.persistentId;
            if (persistentId == Guid.Empty)
                persistentId = Guid.NewGuid();

            imported.SetSourceInfo(relativePath, sourceHash);
            imported.SetRuntimePayload(importResult.artifactBytes);
            PersistMeta(relativePath, importer, imported, persistentId, importResult.dependencies);
            PersistArtifact(relativePath, importResult.artifactBytes);

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Metadata

    private Type? ResolveAssetRuntimeType(AssetMeta meta, Type fallback)
    {
        if (meta.assetTypeStableId != Guid.Empty &&
            TypeCache.TryResolveType(meta.assetTypeStableId, out Type? stableResolved) &&
            stableResolved != null)
        {
            return stableResolved;
        }

        return fallback;
    }

    private Guid? ReadPersistentIdFromMeta(string relativePath)
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

    private void PersistMeta(
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

        byte[] bytes = SerializingState.Serialize(((ISerializable)meta).CaptureState());
        string metaPath = GetMetaPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        WriteAllBytesAtomic(metaPath, bytes);
    }

    private AssetMeta DeserializeMeta(string metaPath)
    {
        byte[] bytes = File.ReadAllBytes(metaPath);
        SerializingState state = SerializingState.Deserialize(bytes);

        var meta = new AssetMeta();
        ((ISerializable)meta).RestoreState(state);
        return meta;
    }

    private void PersistArtifact(string relativePath, byte[] artifactBytes)
    {
        string artifactPath = GetArtifactPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        WriteAllBytesAtomic(artifactPath, artifactBytes ?? []);
    }

    #endregion

    #region Cache

    private void CacheLoaded(string relativePath, AssetObject asset, Guid persistentId)
    {
        lock (m_sync)
        {
            AssetObject? oldByPath = m_loadedCache.First(m_cachePathKey, relativePath);
            if (oldByPath is not null)
                RemoveFromCache(oldByPath);

            if (persistentId != Guid.Empty)
            {
                AssetObject? oldByGuid = m_loadedCache.First(m_cachePersistentIdKey, persistentId);
                if (oldByGuid is not null)
                    RemoveFromCache(oldByGuid);
            }

            IdentityManager.Register(asset, persistentId);
            asset.SetSourceInfo(relativePath, asset.sourceHash);
            Identity identity = GetIdentity(asset);
            m_loadedCache.Add(asset)
                .Set(m_cachePathKey, relativePath)
                .Set(m_cachePersistentIdKey, identity.persistentId);
        }
    }

    private void RemoveFromCache(AssetObject asset)
    {
        m_loadedCache.Remove(asset);
        IdentityManager.Unregister(asset);
    }

    private bool TryGetLoadedAssetByPathNoLock(string normalizedPath, out AssetObject? loaded)
    {
        AssetObject? cached = m_loadedCache.First(m_cachePathKey, normalizedPath);
        if (cached is null)
        {
            loaded = null;
            return false;
        }

        loaded = cached;
        return true;
    }

    #endregion

    #region Reconcile

    private void ReconcileSourceFile(string relativePath)
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

    private void ReconcileLoadedCache(HashSet<string> sourcePaths)
    {
        (string path, Type type, string hash)[] loaded;
        lock (m_sync)
        {
            loaded = m_loadedCache.All()
                .Select(static x => (x.sourcePath, x.GetType(), x.sourceHash))
                .ToArray();
        }

        for (int i = 0; i < loaded.Length; i++)
        {
            (string path, Type type, string hash) entry = loaded[i];
            if (string.IsNullOrWhiteSpace(entry.path))
                continue;

            if (!sourcePaths.Contains(entry.path))
            {
                HandleDeletedSourcePath(entry.path);
                continue;
            }

            string absSourcePath = GetAbsoluteSourcePath(entry.path);
            if (!File.Exists(absSourcePath))
            {
                HandleDeletedSourcePath(entry.path);
                continue;
            }

            string currentHash = ComputeSha256Hex(File.ReadAllBytes(absSourcePath));
            if (string.Equals(currentHash, entry.hash, StringComparison.Ordinal))
                continue;

            lock (m_sync)
            {
                if (TryGetLoadedAssetByPathNoLock(entry.path, out AssetObject? cached))
                    RemoveFromCache(cached);
            }

            try
            {
                _ = TryImportSourceToDisk(entry.path) && LoadByType(entry.path, entry.type);
            }
            catch
            {
                // A full rescan should converge as far as possible even if one asset fails.
            }
        }
    }

    private void CleanupOrphanMetadataAndArtifacts()
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

    #endregion

    #region Rename Helpers

    private void HandleRenamedDirectory(string oldRelativePath, string newRelativePath)
    {
        AssetObject[] affected;
        lock (m_sync)
        {
            affected = m_loadedCache.All()
                .Where(x => IsSameOrUnderPath(x.sourcePath, oldRelativePath))
                .ToArray();

            for (int i = 0; i < affected.Length; i++)
            {
                AssetObject loaded = affected[i];
                Guid persistentId = GetIdentity(loaded).persistentId;
                string remapped = RemapPathPrefix(loaded.sourcePath, oldRelativePath, newRelativePath);
                RemoveFromCache(loaded);
                IdentityManager.Register(loaded, persistentId);
                loaded.SetSourceInfo(remapped, loaded.sourceHash);
                m_loadedCache.Add(loaded)
                    .Set(m_cachePathKey, remapped)
                    .Set(m_cachePersistentIdKey, GetIdentity(loaded).persistentId);
            }
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

    #endregion

    #region Files

    private string GetMetaPath(string relativePath)
        => Path.Combine(assetRoot, relativePath + C_META_POSTFIX);

    private string GetArtifactPath(string relativePath)
        => Path.Combine(artifactRoot, relativePath + C_ARTIFACT_POSTFIX);

    private void CleanupGeneratedFiles(string relativePath)
    {
        TryDeleteFile(GetMetaPath(relativePath));
        TryDeleteFile(GetArtifactPath(relativePath));
    }

    private void MoveGeneratedFiles(string oldRelativePath, string newRelativePath)
    {
        MoveGeneratedFile(GetMetaPath(oldRelativePath), GetMetaPath(newRelativePath));
        MoveGeneratedFile(GetArtifactPath(oldRelativePath), GetArtifactPath(newRelativePath));
    }

    private void MoveGeneratedTree(string oldRelativePath, string newRelativePath)
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

    private void MoveGeneratedTreeFiles(string oldRootDir, string newRootDir, string postfix)
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

    private void MoveGeneratedFile(string oldPath, string newPath)
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

    private void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
            return;

        File.Delete(path);
        TryDeleteEmptyDirectory(Path.GetDirectoryName(path)!);
    }

    private void TryDeleteEmptyDirectory(string absolutePath)
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

    #endregion

    #region Resolution

    private IAssetImporter ResolveImporterByPathOrAssetType(string relativePath, Type assetType)
    {
        lock (m_sync)
        {
            return ResolveImporterByPathOrAssetTypeNoLock(relativePath, assetType);
        }
    }

    private IAssetImporter ResolveImporterByPathOrAssetTypeNoLock(string relativePath, Type assetType)
    {
        string ext = NormalizeExtension(Path.GetExtension(relativePath));
        IReadOnlyList<IAssetImporter> importers = m_importers.All();
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
        IAssetImporter? byType = m_importers.First(m_importerTypeKey, runtimeTypeId);
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

    private static int ResolveRuntimeTypeId(Type type)
    {
        if (TypeCache.TryGetRuntimeTypeId(type, out int runtimeTypeId))
            return runtimeTypeId;

        throw new InvalidOperationException($"Type '{type.FullName}' has no runtime type id in TypeCache.");
    }

    private string GetAbsoluteSourcePath(string normalizedRelativePath)
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

    private static string ComputeSha256Hex(ReadOnlySpan<byte> sourceBytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(sourceBytes, hash);
        return Convert.ToHexString(hash);
    }

    #endregion
}
