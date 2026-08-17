using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly Dictionary<Guid, string> m_pathByPersistentId = new();
    private readonly Lock m_dependencySync = new();
    private readonly DependencyGraph<string, AssetMeta> m_dependencyGraph = new();
    private readonly Dictionary<string, HashSet<string>> m_dependenciesByPath = new(StringComparer.OrdinalIgnoreCase);

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
    public AssetObject? Load(string relativePath, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);

        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return TryLoadFromDiskCache(normalized, requestedAssetType, out AssetObject? asset) ? asset : null;
    }

    /// <summary>
    /// Tries to load an asset from existing metadata and artifact files into memory.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="requestedAssetType">Required asset base type.</param>
    /// <param name="asset">Loaded asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public bool TryLoad(string relativePath, Type requestedAssetType, out AssetObject? asset)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        string normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            asset = null;
            return false;
        }
        return TryLoadFromDiskCache(normalized, requestedAssetType, out asset);
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
    /// Resolves a loaded asset or loads it through the persistent identity catalog.
    /// </summary>
    /// <param name="identity">Persistent asset identity.</param>
    /// <param name="requestedAssetType">Required asset base type.</param>
    /// <returns>The resolved asset, or <see langword="null"/> when the catalog has no compatible entry.</returns>
    public AssetObject? ResolveOrLoad(Identity identity, Type requestedAssetType)
    {
        AssetObject? loaded = Resolve(identity, requestedAssetType);
        if (loaded is not null)
            return loaded;
        string? path = FindPath(identity.persistentId);
        return path is null ? null : Load(path, requestedAssetType);
    }

    /// <summary>
    /// Resolves the current source path for a persistent asset identity.
    /// </summary>
    /// <param name="persistentId">Persistent asset identity.</param>
    /// <returns>The relative source path, or <see langword="null"/> when the catalog has no entry.</returns>
    public string? FindPath(Guid persistentId)
    {
        if (persistentId == Guid.Empty)
            return null;
        lock (m_sync)
        {
            if (m_pathByPersistentId.TryGetValue(persistentId, out string? cachedPath) &&
                File.Exists(GetMetaPath(cachedPath)))
            {
                return cachedPath;
            }
        }

        string[] metaFiles = Directory.GetFiles(assetRoot, "*" + C_META_POSTFIX, SearchOption.AllDirectories);
        for (int i = 0; i < metaFiles.Length; i++)
        {
            string relativeMeta = NormalizeRelativePath(Path.GetRelativePath(assetRoot, metaFiles[i]));
            string relativePath = relativeMeta[..^C_META_POSTFIX.Length];
            Guid? candidate = ReadPersistentIdFromMeta(relativePath);
            if (candidate is not Guid candidateId)
                continue;
            lock (m_sync)
                m_pathByPersistentId[candidateId] = relativePath;
            if (candidateId == persistentId)
                return relativePath;
        }

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
        if (persistentId is Guid id)
        {
            lock (m_sync)
                m_pathByPersistentId[id] = normalized;
        }
        return persistentId.HasValue ? new Identity(persistentId.Value) : default;
    }

    /// <summary>
    /// Tries to resolve the concrete asset type recorded for a source path without loading the asset.
    /// </summary>
    /// <param name="relativePath">Source path relative to the asset root.</param>
    /// <param name="assetType">Resolved asset type when metadata is available.</param>
    /// <returns><see langword="true"/> when a concrete asset type was resolved.</returns>
    public bool TryGetAssetType(string relativePath, out Type? assetType)
    {
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            AssetObject? loaded = m_loadedCache.First(m_cachePathKey, normalized);
            if (loaded is not null)
            {
                assetType = loaded.GetType();
                return true;
            }
        }

        try
        {
            string metaPath = GetMetaPath(normalized);
            if (!File.Exists(metaPath))
            {
                assetType = null;
                return false;
            }

            AssetMeta meta = DeserializeMeta(metaPath);
            IAssetImporter importer = ResolveImporterByPathOrAssetType(normalized, typeof(AssetObject));
            assetType = ResolveAssetRuntimeType(meta, importer.targetAssetType);
            return assetType is not null && typeof(AssetObject).IsAssignableFrom(assetType);
        }
        catch
        {
            assetType = null;
            return false;
        }
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
        IReadOnlyList<string> dependencyOrder = m_dependencyGraph.TopologicalSort();
        lock (m_sync)
        {
            var loadedByPath = m_loadedCache.All().ToDictionary(
                static asset => asset.sourcePath,
                StringComparer.OrdinalIgnoreCase);
            for (int i = dependencyOrder.Count - 1; i >= 0; i--)
            {
                if (loadedByPath.Remove(dependencyOrder[i], out AssetObject? asset))
                    RemoveFromCache(asset);
            }
            foreach (AssetObject asset in loadedByPath.Values)
                RemoveFromCache(asset);
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
        lock (m_dependencySync)
        {
            m_dependencyGraph.Clear();
            m_dependenciesByPath.Clear();
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
            if (TryGetLoadedAssetByPathNoLock(path, out AssetObject? loaded) && loaded is not null)
                RemoveFromCache(loaded);
        }

        CleanupGeneratedFiles(path);
        RemoveDependencyNode(path);
    }

    /// <summary>
    /// Handles a created or changed source path by importing or reloading it.
    /// </summary>
    public void HandleCreatedOrChangedSourcePath(string relativePath)
    {
        string path = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(path))
            return;
        m_dependencyGraph.Invalidate(path);

        string absSourcePath = GetAbsoluteSourcePath(path);
        if (!File.Exists(absSourcePath))
            return;

        AssetObject? loaded = null;
        lock (m_sync)
        {
            if (TryGetLoadedAssetByPathNoLock(path, out AssetObject? cached) && cached is not null)
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
            lock (m_sync)
            {
                AssetObject? cached = m_loadedCache.First(m_cachePathKey, relativePath);
                if (cached is not null && requestedAssetType.IsAssignableFrom(cached.GetType()))
                {
                    asset = cached;
                    return true;
                }
            }

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
            if (Activator.CreateInstance(runtimeType, nonPublic: true) is not AssetObject restored)
            {
                asset = null;
                return false;
            }

            if (meta.assetStateBytes.Length > 0)
            {
                _ = SerializationManager.Decode(meta.assetStateBytes, reader =>
                {
                    reader.RestoreProperties(restored);
                    return true;
                });
            }

            restored.SetSourceInfo(relativePath, sourceHash);
            restored.SetRuntimePayload(artifactBytes);
            restored.SetDependenciesInternal(ResolveDependencyDescriptors(meta.dependencies));

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
            imported.SetDependenciesInternal(ResolveDependencyDescriptors(importResult.dependencies));
            PersistMeta(relativePath, importer, imported, persistentId, importResult.dependencies);
            PersistArtifact(relativePath, importResult.artifactBytes);

            return true;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Asset dependency cycle", StringComparison.Ordinal))
        {
            throw;
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
            assetStateBytes = SerializationManager.Encode(writer => writer.WriteProperties(asset))
        };

        UpdateDependencyGraph(relativePath, meta.dependencies, meta);

        StableTypeIdAttribute? stableTypeAttribute = asset.GetType()
            .GetCustomAttribute<StableTypeIdAttribute>(inherit: false);
        if (stableTypeAttribute is null || !Guid.TryParse(stableTypeAttribute.id, out Guid stableTypeId))
            throw new InvalidOperationException($"Asset type '{asset.GetType().FullName}' requires StableTypeId before persistence.");
        meta.assetTypeStableId = stableTypeId;

        byte[] bytes = SerializationManager.Serialize(meta);
        string metaPath = GetMetaPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        WriteAllBytesAtomic(metaPath, bytes);
        lock (m_sync)
            m_pathByPersistentId[persistentId] = relativePath;
    }

    private AssetMeta DeserializeMeta(string metaPath)
    {
        byte[] bytes = File.ReadAllBytes(metaPath);
        AssetMeta meta = SerializationManager.Deserialize<AssetMeta>(bytes);
        if (!string.IsNullOrWhiteSpace(meta.relativePath))
            UpdateDependencyGraph(meta.relativePath, meta.dependencies, meta);
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
            m_pathByPersistentId[identity.persistentId] = relativePath;
        }
    }

    private void RemoveFromCache(AssetObject asset)
    {
        m_loadedCache.Remove(asset);
        IdentityManager.Unregister(asset);
    }

    private void UpdateDependencyGraph(string relativePath, IEnumerable<string> dependencies, AssetMeta meta)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        var nextDependencies = new HashSet<string>(
            dependencies.Select(NormalizeRelativePath).Where(static path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.OrdinalIgnoreCase);

        lock (m_dependencySync)
        {
            HashSet<string> previousDependencies = m_dependenciesByPath.TryGetValue(normalizedPath, out HashSet<string>? previous)
                ? new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dependency in previousDependencies)
                m_dependencyGraph.RemoveDependency(normalizedPath, dependency);
            m_dependencyGraph.AddNode(normalizedPath);
            foreach (string dependency in nextDependencies)
                m_dependencyGraph.AddDependency(normalizedPath, dependency);

            try
            {
                _ = m_dependencyGraph.TopologicalSort();
            }
            catch (InvalidOperationException exception)
            {
                string cycle = FindDependencyCycle(normalizedPath, nextDependencies);
                foreach (string dependency in nextDependencies)
                    m_dependencyGraph.RemoveDependency(normalizedPath, dependency);
                foreach (string dependency in previousDependencies)
                    m_dependencyGraph.AddDependency(normalizedPath, dependency);
                throw new InvalidOperationException(
                    $"Asset dependency cycle detected while registering '{normalizedPath}': {cycle}.",
                    exception);
            }

            m_dependenciesByPath[normalizedPath] = nextDependencies;
            m_dependencyGraph.Invalidate(normalizedPath);
            _ = m_dependencyGraph.GetOrUpdate(normalizedPath, _ => meta);
        }
    }

    private string FindDependencyCycle(string changedPath, IReadOnlyCollection<string> changedDependencies)
    {
        var adjacency = m_dependenciesByPath.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        adjacency[changedPath] = changedDependencies;
        foreach (string dependency in changedDependencies)
        {
            if (!adjacency.ContainsKey(dependency))
                adjacency.Add(dependency, Array.Empty<string>());
        }

        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        foreach (string node in adjacency.Keys)
        {
            if (Visit(node, out string cycle))
                return cycle;
        }
        return changedPath;

        bool Visit(string node, out string cycle)
        {
            states.TryGetValue(node, out int state);
            if (state == 2)
            {
                cycle = string.Empty;
                return false;
            }
            if (state == 1)
            {
                int start = stack.FindIndex(path =>
                    string.Equals(path, node, StringComparison.OrdinalIgnoreCase));
                cycle = string.Join(" -> ", stack.Skip(Math.Max(0, start)).Append(node));
                return true;
            }

            states[node] = 1;
            stack.Add(node);
            if (adjacency.TryGetValue(node, out IReadOnlyCollection<string>? dependencies))
            {
                foreach (string dependency in dependencies)
                {
                    if (Visit(dependency, out cycle))
                        return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            states[node] = 2;
            cycle = string.Empty;
            return false;
        }
    }

    private void RemoveDependencyNode(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        lock (m_dependencySync)
        {
            m_dependenciesByPath.Remove(normalizedPath);
            m_dependencyGraph.RemoveNode(normalizedPath);
        }
    }

    private AssetDependency[] ResolveDependencyDescriptors(IEnumerable<string> dependencyPaths)
    {
        var descriptors = new List<AssetDependency>();
        var seen = new HashSet<Guid>();
        foreach (string dependencyPath in dependencyPaths)
        {
            string normalized = NormalizeRelativePath(dependencyPath);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            string metaPath = GetMetaPath(normalized);
            if (!File.Exists(metaPath))
                continue;

            try
            {
                AssetMeta meta = SerializationManager.Deserialize<AssetMeta>(File.ReadAllBytes(metaPath));
                if (meta.persistentId == Guid.Empty || !seen.Add(meta.persistentId))
                    continue;
                descriptors.Add(new AssetDependency(
                    meta.persistentId,
                    meta.assetTypeStableId,
                    normalized));
                lock (m_sync)
                    m_pathByPersistentId[meta.persistentId] = normalized;
            }
            catch
            {
                // A stale dependency metadata file remains represented by its path in AssetMeta.
            }
        }

        return descriptors.OrderBy(static dependency => dependency.persistentId).ToArray();
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
                        _ = SerializationManager.Decode(meta.assetStateBytes, static _ => true);
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
                if (TryGetLoadedAssetByPathNoLock(entry.path, out AssetObject? cached) && cached is not null)
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
