using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Storage;

using IOFile = System.IO.File;

namespace Inno.Assets.Loader;

/// <summary>
/// Coordinates importing, persistent cataloging, canonical loading, reloading and collection
/// for one source and artifact root pair.
/// </summary>
public sealed class AssetLoader : IDisposable
{
    internal const string C_META_POSTFIX = ".imeta";
    internal const string C_ARTIFACT_POSTFIX = ".abin";

    [ThreadStatic]
    private static AssetLoader? t_activeLoader;

    private readonly SemaphoreSlim m_operationGate = new(1, 1);
    private readonly object m_asyncSync = new();
    private readonly Dictionary<string, Task<AssetObject?>> m_inFlightPathLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Task<AssetObject?>> m_inFlightIdLoads = [];
    private readonly AssetImporterRegistry m_importers = new();
    private readonly Dictionary<string, AssetRecord> m_recordsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, AssetRecord> m_recordsById = [];
    private readonly Dictionary<Guid, WeakReference<AssetObject>> m_missingAssets = [];
    private readonly DependencyGraph<Guid> m_runtimeGraph = new();
    private readonly DependencyGraph<string> m_importGraph = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<AssetObject, AssetDependencySet> m_dependencyRetention = new();
    private readonly HashSet<string> m_activeImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> m_pendingImportIds = new(StringComparer.OrdinalIgnoreCase);

    private bool m_disposed;

    /// <summary>Creates an asset loader for one source and artifact root pair.</summary>
    /// <param name="assetRoot">The absolute source root.</param>
    /// <param name="artifactRoot">The absolute generated artifact root.</param>
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
    }

    /// <summary>Gets the absolute source root.</summary>
    public string assetRoot { get; }

    /// <summary>Gets the absolute generated artifact root.</summary>
    public string artifactRoot { get; }

    /// <summary>Occurs after a loaded canonical asset is updated in place.</summary>
    public event Action<AssetObject>? AssetReloaded;

    /// <summary>Imports one source file into metadata and a runtime artifact.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns><see langword="true"/> when an importer handled the source.</returns>
    public bool Import(string relativePath)
        => Execute(() => ImportLocked(NormalizeRelativePath(relativePath)));

    /// <summary>Reconciles source files, metadata, artifacts and the in-memory catalog.</summary>
    public void Rescan()
        => Execute(RescanLocked);

    /// <summary>Loads a canonical asset by source-relative path.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <returns>The canonical asset, or <see langword="null"/> when unavailable or incompatible.</returns>
    public AssetObject? Load(string relativePath, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        return Execute(() => LoadPathLocked(NormalizeRelativePath(relativePath), requestedAssetType));
    }

    /// <summary>Tries to load a canonical asset by source-relative path.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public bool TryLoad(string relativePath, Type requestedAssetType, out AssetObject? asset)
    {
        asset = Load(relativePath, requestedAssetType);
        return asset is not null;
    }

    /// <summary>Loads a canonical asset by persistent identity.</summary>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <returns>The canonical asset, or <see langword="null"/> when unavailable or incompatible.</returns>
    public AssetObject? Load(Guid persistentId, Type requestedAssetType)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        return Execute(() => LoadIdLocked(persistentId, requestedAssetType));
    }

    /// <summary>Tries to load a canonical asset by persistent identity.</summary>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <param name="asset">The canonical asset when successful.</param>
    /// <returns><see langword="true"/> when a compatible asset was loaded.</returns>
    public bool TryLoad(Guid persistentId, Type requestedAssetType, out AssetObject? asset)
    {
        asset = Load(persistentId, requestedAssetType);
        return asset is not null;
    }

    /// <summary>Asynchronously loads a canonical asset by source-relative path.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset, or <see langword="null"/> when unavailable or incompatible.</returns>
    public ValueTask<AssetObject?> LoadAsync(
        string relativePath,
        Type requestedAssetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        string normalized = NormalizeRelativePath(relativePath);
        Task<AssetObject?> operation;
        lock (m_asyncSync)
        {
            if (!m_inFlightPathLoads.TryGetValue(normalized, out operation!))
            {
                operation = Task.Run(() => Load(normalized, typeof(AssetObject)));
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

    /// <summary>Asynchronously loads a canonical asset by persistent identity.</summary>
    /// <param name="persistentId">The persistent asset identity.</param>
    /// <param name="requestedAssetType">The required assignable asset type.</param>
    /// <param name="cancellationToken">Cancellation for the current caller's wait.</param>
    /// <returns>The canonical asset, or <see langword="null"/> when unavailable or incompatible.</returns>
    public ValueTask<AssetObject?> LoadAsync(
        Guid persistentId,
        Type requestedAssetType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAssetType);
        Task<AssetObject?> operation;
        lock (m_asyncSync)
        {
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

    /// <summary>Resolves a serialized reference or creates a persistent missing placeholder.</summary>
    /// <param name="persistentId">The referenced persistent identity.</param>
    /// <param name="stableTypeId">The referenced stable asset type identity.</param>
    /// <param name="lastKnownPath">The last known source-relative path.</param>
    /// <param name="expectedType">The declared destination type.</param>
    /// <returns>A compatible canonical asset or missing placeholder.</returns>
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

    /// <summary>Saves an asset back to its current source path.</summary>
    /// <param name="asset">The asset to export.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public bool Save(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.sourcePath))
            throw new InvalidOperationException("An unsaved asset requires an explicit source-relative path.");
        return Save(asset.sourcePath, asset);
    }

    /// <summary>Saves an asset to its initial or existing source-relative path.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="asset">The asset to export.</param>
    /// <returns><see langword="true"/> when an importer exported the asset.</returns>
    public bool Save(string relativePath, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string normalized = NormalizeRelativePath(relativePath);
        return Execute(() => SaveLocked(normalized, asset));
    }

    /// <summary>Applies normalized source file changes to the persistent catalog.</summary>
    /// <param name="changes">The normalized source changes.</param>
    public void ApplySourceChanges(IReadOnlyList<AssetChangedEvent> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Execute(() => ApplySourceChangesLocked(changes));
    }

    /// <summary>Tries to resolve a persistent identity without loading the asset.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="persistentId">The resolved identity.</param>
    /// <returns><see langword="true"/> when catalog metadata exists.</returns>
    public bool TryGetPersistentId(string relativePath, out Guid persistentId)
    {
        string normalized = NormalizeRelativePath(relativePath);
        Guid result = Execute(() => FindRecordLocked(normalized)?.persistentId ?? Guid.Empty);
        persistentId = result;
        return result != Guid.Empty;
    }

    /// <summary>Tries to resolve the concrete asset type without loading it.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="assetType">The resolved type.</param>
    /// <returns><see langword="true"/> when the type can be resolved.</returns>
    public bool TryGetAssetType(string relativePath, out Type? assetType)
    {
        string normalized = NormalizeRelativePath(relativePath);
        Type? result = Execute(() => ResolveRecordType(FindRecordLocked(normalized)));
        assetType = result;
        return result is not null;
    }

    /// <summary>Gets source paths of all canonical loaded assets.</summary>
    /// <returns>A stable source-relative path snapshot.</returns>
    public IReadOnlyList<string> GetLoadedPaths()
        => Execute(() => m_recordsByPath.Values
            .Where(static record => record.asset is not null)
            .Select(static record => record.relativePath)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray());

    /// <summary>Gets direct or transitive runtime dependencies of an asset.</summary>
    /// <param name="asset">The asset to query.</param>
    /// <param name="recursive">Whether transitive dependencies should be included.</param>
    /// <returns>The persistent dependency descriptors.</returns>
    public IReadOnlyList<AssetDependency> GetDependencies(AssetObject asset, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Execute(() => GetDependenciesLocked(asset.identity.persistentId, recursive));
    }

    /// <summary>Gets an engine-known reference diagnostic snapshot.</summary>
    /// <param name="asset">The asset to inspect.</param>
    /// <returns>The reference diagnostic snapshot.</returns>
    public AssetReferenceInfo GetReferenceInfo(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Execute(() => GetReferenceInfoLocked(asset));
    }

    /// <summary>Collects canonical assets that have no external managed references.</summary>
    /// <returns>The number of released canonical assets.</returns>
    public int UnloadUnusedAssets()
        => Execute(UnloadUnusedAssetsLocked);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;
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

        Guid persistentId = FindRecordLocked(relativePath)?.persistentId
            ?? m_pendingImportIds.GetValueOrDefault(relativePath);
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();
        m_pendingImportIds[relativePath] = persistentId;
        m_activeImports.Add(relativePath);
        try
        {
            byte[] sourceBytes = IOFile.ReadAllBytes(sourcePath);
            ImportBuild build = BuildImportLocked(relativePath, sourceBytes, importer, persistentId);
            try
            {
                CommitBuildLocked(build, writeSource: false, sourceBytes);
            }
            finally
            {
                AssetRuntimeAccess.Release(build.asset);
            }
            return true;
        }
        finally
        {
            m_activeImports.Remove(relativePath);
            m_pendingImportIds.Remove(relativePath);
        }
    }

    private ImportBuild BuildImportLocked(
        string relativePath,
        byte[] sourceBytes,
        AssetImporter importer,
        Guid persistentId)
    {
        string sourceHash = ComputeSha256Hex(sourceBytes);
        var context = new AssetImportContext(
            relativePath,
            GetSourcePath(relativePath),
            sourceBytes,
            sourceHash);
        AssetImportProduct product = importer.ImportInternal(context);
        if (!importer.targetAssetType.IsInstanceOfType(product.asset))
        {
            throw new InvalidOperationException(
                $"Importer '{importer.GetType().FullName}' returned '{product.asset.GetType().FullName}' " +
                $"instead of '{importer.targetAssetType.FullName}'.");
        }
        if (!TypeCacheManager.TryGetStableTypeId(product.asset.GetType(), out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"Imported asset type '{product.asset.GetType().FullName}' requires a StableTypeId.");
        }

        AssetRuntimeAccess.Initialize(product.asset, relativePath, sourceHash, product.runtimePayload, false, 1);
        AssetDependency[] runtimeDependencies = ResolveDeclaredDependenciesLocked(context);
        byte[] state = SerializationManager.Encode(writer => writer.WriteProperties(product.asset));
        var meta = new AssetMeta
        {
            persistentId = persistentId,
            relativePath = relativePath,
            sourceHash = sourceHash,
            importerId = importer.importerId,
            importerVersion = importer.version,
            stableAssetTypeId = stableTypeId,
            assetStateBytes = state,
            runtimeDependencies = runtimeDependencies.Select(ToData).ToArray(),
            importDependencies = context.importDependencies.Select(ToData).ToArray()
        };
        ValidateImportDependenciesLocked(meta);
        return new ImportBuild(meta, product.asset, product.runtimePayload.ToArray(), runtimeDependencies);
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
                throw new InvalidOperationException(
                    $"Loaded asset '{build.meta.relativePath}' cannot change type from " +
                    $"'{canonical.GetType().FullName}' to '{build.asset.GetType().FullName}'.");
            }
            previousState = SerializationManager.Encode(writer => writer.WriteProperties(canonical));
            previousPayload = canonical.runtimePayload.ToArray();
            previousPath = canonical.sourcePath;
            previousHash = AssetRuntimeAccess.GetSourceHash(canonical);
            previousVersion = canonical.contentVersion;
            previousMissing = canonical.isMissing;
            try
            {
                SerializationManager.Decode(build.meta.assetStateBytes, reader =>
                {
                    reader.RestoreProperties(canonical);
                    return 0;
                });
                AssetRuntimeAccess.Initialize(
                    canonical,
                    build.meta.relativePath,
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
        string artifactPath = GetArtifactPath(build.meta.relativePath);
        FileSnapshot sourceSnapshot = CaptureFile(sourcePath);
        FileSnapshot metaSnapshot = CaptureFile(metaPath);
        FileSnapshot artifactSnapshot = CaptureFile(artifactPath);
        try
        {
            if (writeSource)
                WriteAtomic(sourcePath, sourceBytes);
            WriteAtomic(metaPath, SerializationManager.Serialize(build.meta));
            WriteAtomic(artifactPath, build.payload);
        }
        catch
        {
            if (writeSource)
                RestoreFile(sourcePath, sourceSnapshot);
            RestoreFile(metaPath, metaSnapshot);
            RestoreFile(artifactPath, artifactSnapshot);
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
        if (canonical is not null)
        {
            AttachDependenciesLocked(record);
            PublishReloaded(canonical);
        }
    }

    private bool SaveLocked(string relativePath, AssetObject asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.sourcePath) &&
            !string.Equals(NormalizeRelativePath(asset.sourcePath), relativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset '{asset.sourcePath}' cannot be saved to unrelated path '{relativePath}' without creating a new asset.");
        }
        AssetImporter? importer = m_importers.FindByPath(relativePath);
        if (importer is null || !importer.targetAssetType.IsInstanceOfType(asset))
            return false;
        if (!importer.TryExportInternal(asset, out byte[] sourceBytes))
            return false;
        Guid persistentId = asset.identity.persistentId;
        if (persistentId == Guid.Empty)
            persistentId = Guid.NewGuid();
        ImportBuild build = BuildImportLocked(relativePath, sourceBytes, importer, persistentId);
        AssetRecord? provisionalRecord = null;
        bool registeredHere = false;
        if (string.IsNullOrWhiteSpace(asset.sourcePath))
        {
            IdentityManager.InitializePersistentIdentity(asset, persistentId);
            registeredHere = IdentityManager.Register(asset, persistentId);
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
                AssetRuntimeAccess.Release(build.asset);
            }
        }
        catch
        {
            if (provisionalRecord is not null)
                RemoveRecordLocked(provisionalRecord, removeGeneratedFiles: false);
            if (registeredHere)
                IdentityManager.Unregister(asset);
            throw;
        }
        AssetRecord committed = m_recordsByPath[relativePath];
        if (committed.asset is null)
        {
            committed.asset = asset;
            if (asset.identity.runtimeId is null)
                IdentityManager.Register(asset, persistentId);
            AssetRuntimeAccess.Initialize(asset, relativePath, build.meta.sourceHash, build.payload, false, 1);
            AttachDependenciesLocked(committed);
        }
        return true;
    }

    private AssetObject? LoadPathLocked(string relativePath, Type requestedAssetType)
    {
        AssetRecord? record = FindRecordLocked(relativePath);
        if (record is null || IsStale(record))
        {
            if (!ImportLocked(relativePath))
                return null;
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
        return LoadRecordLocked(record, requestedAssetType);
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
        IdentityManager.InitializePersistentIdentity(shell, record.persistentId);
        IdentityManager.Register(shell, record.persistentId);
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
        SerializationManager.Decode(record.meta.assetStateBytes, reader =>
        {
            reader.RestoreProperties(asset);
            return 0;
        });
        byte[] payload = IOFile.Exists(GetArtifactPath(record.relativePath))
            ? IOFile.ReadAllBytes(GetArtifactPath(record.relativePath))
            : record.payload;
        record.payload = payload;
        AssetRuntimeAccess.Initialize(asset, record.relativePath, record.meta.sourceHash, payload, false, 1);
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
                    dependency.stableTypeId,
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
        if (m_missingAssets.TryGetValue(persistentId, out WeakReference<AssetObject>? weak) &&
            weak.TryGetTarget(out AssetObject? existing) && expectedType.IsInstanceOfType(existing))
        {
            return existing;
        }
        Type type = ResolveDependencyExpectedType(new AssetDependency(persistentId, stableTypeId, lastKnownPath));
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
        IdentityManager.InitializePersistentIdentity(missing, persistentId);
        AssetRuntimeAccess.Initialize(missing, lastKnownPath, string.Empty, ReadOnlyMemory<byte>.Empty, true, 0);
        m_missingAssets[persistentId] = new WeakReference<AssetObject>(missing);
        return missing;
    }

    private AssetDependency[] ResolveDeclaredDependenciesLocked(AssetImportContext context)
    {
        var result = context.runtimeDependencies.ToDictionary(static value => value.persistentId);
        foreach (string path in context.runtimeDependencyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = NormalizeRelativePath(path);
            if (m_activeImports.Contains(normalized))
            {
                Guid pendingId = m_pendingImportIds[normalized];
                AssetImporter? pendingImporter = m_importers.FindByPath(normalized);
                Guid stableType = pendingImporter is not null &&
                    TypeCacheManager.TryGetStableTypeId(pendingImporter.targetAssetType, out Guid typeId)
                    ? typeId
                    : Guid.Empty;
                result[pendingId] = new AssetDependency(pendingId, stableType, normalized);
                continue;
            }
            AssetRecord? dependencyRecord = FindRecordLocked(normalized);
            if ((dependencyRecord is null || IsStale(dependencyRecord)) && !ImportLocked(normalized))
            {
                throw new InvalidOperationException(
                    $"Runtime dependency '{normalized}' referenced by '{context.relativePath}' cannot be imported.");
            }
            dependencyRecord = FindRecordLocked(normalized)
                ?? throw new InvalidOperationException($"Runtime dependency '{normalized}' has no metadata.");
            result[dependencyRecord.persistentId] = new AssetDependency(
                dependencyRecord.persistentId,
                dependencyRecord.stableTypeId,
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

    private void RescanLocked()
    {
        LoadCatalogLocked();
        string[] sourceFiles = Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (string sourceFile in sourceFiles)
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(assetRoot, sourceFile));
            AssetImporter? importer = m_importers.FindByPath(relative);
            if (importer is null)
                continue;
            AssetRecord? record = FindRecordLocked(relative);
            if (record is null ||
                record.asset?.isMissing == true ||
                IsStale(record) ||
                !IOFile.Exists(GetArtifactPath(relative)))
            {
                ImportLocked(relative);
            }
        }

        foreach (AssetRecord record in m_recordsByPath.Values.ToArray())
        {
            if (IOFile.Exists(GetSourcePath(record.relativePath)))
                continue;
            HandleDeletedLocked(record.relativePath);
        }
    }

    private void LoadCatalogLocked()
    {
        foreach (string metaPath in Directory.GetFiles(assetRoot, "*" + C_META_POSTFIX, SearchOption.AllDirectories))
        {
            string relativeMeta = NormalizeRelativePath(Path.GetRelativePath(assetRoot, metaPath));
            string relative = relativeMeta[..^C_META_POSTFIX.Length];
            try
            {
                AssetMeta meta = SerializationManager.Deserialize<AssetMeta>(IOFile.ReadAllBytes(metaPath));
                if (meta.schemaVersion != AssetMeta.C_SCHEMA_VERSION || meta.persistentId == Guid.Empty)
                    continue;
                AssetRecord record = FindRecordLocked(relative) ?? new AssetRecord();
                record.relativePath = relative;
                record.persistentId = meta.persistentId;
                record.stableTypeId = meta.stableAssetTypeId;
                record.meta = meta;
                record.payload = IOFile.Exists(GetArtifactPath(relative))
                    ? IOFile.ReadAllBytes(GetArtifactPath(relative))
                    : [];
                AddOrReplaceRecordLocked(record);
                UpdateGraphsLocked(record);
            }
            catch
            {
                // Generated metadata is a cache and is repaired from source by Rescan.
            }
        }
    }

    private void ApplySourceChangesLocked(IReadOnlyList<AssetChangedEvent> changes)
    {
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
    }

    private void HandleRenameLocked(string oldPath, string newPath)
    {
        string oldNormalized = NormalizeRelativePath(oldPath);
        string newNormalized = NormalizeRelativePath(newPath);
        AssetRecord? record = FindRecordLocked(oldNormalized);
        if (record is null)
        {
            ImportLocked(newNormalized);
            return;
        }
        m_recordsByPath.Remove(oldNormalized);
        string oldMeta = GetMetaPath(oldNormalized);
        string newMeta = GetMetaPath(newNormalized);
        string oldArtifact = GetArtifactPath(oldNormalized);
        string newArtifact = GetArtifactPath(newNormalized);
        MoveGeneratedFile(oldMeta, newMeta);
        MoveGeneratedFile(oldArtifact, newArtifact);
        record.relativePath = newNormalized;
        record.meta.relativePath = newNormalized;
        if (record.asset is not null)
            AssetRuntimeAccess.UpdateSourcePath(record.asset, newNormalized);
        m_recordsByPath[newNormalized] = record;
        WriteAtomic(newMeta, SerializationManager.Serialize(record.meta));
    }

    private void HandleDeletedLocked(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        AssetRecord? record = FindRecordLocked(normalized);
        if (record is null)
            return;
        DeleteIfExists(GetMetaPath(normalized));
        DeleteIfExists(GetArtifactPath(normalized));
        if (record.asset is null)
        {
            RemoveRecordLocked(record);
            return;
        }
        AssetRuntimeAccess.Initialize(
            record.asset,
            normalized,
            string.Empty,
            ReadOnlyMemory<byte>.Empty,
            true,
            record.asset.contentVersion + 1);
        record.payload = [];
        PublishReloaded(record.asset);
    }

    private IReadOnlyList<AssetDependency> GetDependenciesLocked(Guid persistentId, bool recursive)
    {
        if (!m_recordsById.TryGetValue(persistentId, out AssetRecord? record))
            return Array.Empty<AssetDependency>();
        IEnumerable<Guid> ids = recursive
            ? m_runtimeGraph.GetDependencies(persistentId, recursive: true)
            : m_runtimeGraph.GetDependencies(persistentId);
        return ids.Select(id => m_recordsById.TryGetValue(id, out AssetRecord? dependency)
                ? new AssetDependency(id, dependency.stableTypeId, dependency.relativePath)
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
            locations.Add(AssetRuntimeAccess.CreateReferenceLocation(
                AssetReferenceKind.AssetDependency,
                dependentId,
                dependent.relativePath,
                "runtimeDependencies"));
        }
        m_recordsById.TryGetValue(id, out AssetRecord? record);
        return AssetRuntimeAccess.CreateReferenceInfo(
            id,
            asset.sourcePath,
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
            RemoveRecordLocked(candidate.record, removeGeneratedFiles: false);
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
            AssetRuntimeAccess.Release(record.asset);
            IdentityManager.Unregister(record.asset);
            record.asset = null;
        }
        m_recordsByPath.Clear();
        m_recordsById.Clear();
        m_runtimeGraph.Clear();
        m_importGraph.Clear();
        m_missingAssets.Clear();
        m_importers.Dispose();
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
        if (!IOFile.Exists(metaPath))
            return null;
        try
        {
            AssetMeta meta = SerializationManager.Deserialize<AssetMeta>(IOFile.ReadAllBytes(metaPath));
            if (meta.schemaVersion != AssetMeta.C_SCHEMA_VERSION)
                return null;
            record = new AssetRecord
            {
                relativePath = relativePath,
                persistentId = meta.persistentId,
                stableTypeId = meta.stableAssetTypeId,
                meta = meta,
                payload = IOFile.Exists(GetArtifactPath(relativePath))
                    ? IOFile.ReadAllBytes(GetArtifactPath(relativePath))
                    : []
            };
            AddOrReplaceRecordLocked(record);
            UpdateGraphsLocked(record);
            return record;
        }
        catch
        {
            return null;
        }
    }

    private void AddOrReplaceRecordLocked(AssetRecord record)
    {
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
        m_recordsById.Remove(record.persistentId);
        m_runtimeGraph.RemoveNode(record.persistentId);
        m_importGraph.RemoveNode(record.relativePath);
        if (removeGeneratedFiles)
        {
            DeleteIfExists(GetMetaPath(record.relativePath));
            DeleteIfExists(GetArtifactPath(record.relativePath));
        }
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
        return TypeCacheManager.TryResolveType(record.stableTypeId, out Type? type) &&
            type is not null && typeof(AssetObject).IsAssignableFrom(type)
            ? type
            : m_importers.FindById(record.meta.importerId)?.targetAssetType;
    }

    private Type ResolveDependencyExpectedType(AssetDependency dependency)
    {
        if (dependency.stableTypeId != Guid.Empty &&
            TypeCacheManager.TryResolveType(dependency.stableTypeId, out Type? type) &&
            type is not null && typeof(AssetObject).IsAssignableFrom(type))
        {
            return type;
        }
        return typeof(MissingAsset);
    }

    private bool IsStale(AssetRecord record)
    {
        string sourcePath = GetSourcePath(record.relativePath);
        if (!IOFile.Exists(sourcePath) || record.meta.schemaVersion != AssetMeta.C_SCHEMA_VERSION)
            return true;
        if (record.meta.importerVersion != m_importers.FindById(record.meta.importerId)?.version)
            return true;
        if (record.importerGeneration != m_importers.GetGeneration(record.meta.importerId))
            return true;
        return !string.Equals(
            record.meta.sourceHash,
            ComputeSha256Hex(IOFile.ReadAllBytes(sourcePath)),
            StringComparison.Ordinal);
    }

    private void RollbackLoadLocked(AssetLoadTransaction transaction)
    {
        foreach (AssetRecord record in transaction.createdRecords.AsEnumerable().Reverse())
        {
            if (record.asset is not null)
            {
                AssetRuntimeAccess.Release(record.asset);
                IdentityManager.Unregister(record.asset);
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
        SerializationManager.Decode(state, reader =>
        {
            reader.RestoreProperties(canonical);
            return 0;
        });
        AssetRuntimeAccess.Initialize(canonical, sourcePath, sourceHash, payload, isMissing, version);
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
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Asset paths must be relative to the configured source root.", nameof(relativePath));
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string fullPath = Path.GetFullPath(Path.Combine(assetRoot, normalized));
        string rootPrefix = assetRoot.EndsWith(Path.DirectorySeparatorChar)
            ? assetRoot
            : assetRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) &&
            !string.Equals(fullPath, assetRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException("Asset paths cannot escape the configured source root.", nameof(relativePath));
        }
        return Path.GetRelativePath(assetRoot, fullPath).Replace('\\', '/');
    }

    private string GetSourcePath(string relativePath) => Path.Combine(assetRoot, relativePath);
    private string GetMetaPath(string relativePath) => GetSourcePath(relativePath) + C_META_POSTFIX;
    private string GetArtifactPath(string relativePath) => Path.Combine(artifactRoot, relativePath + C_ARTIFACT_POSTFIX);

    private static string ComputeSha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private bool IsInternalGeneratedPath(string relativePath)
        => relativePath.EndsWith(C_META_POSTFIX, StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(C_ARTIFACT_POSTFIX, StringComparison.OrdinalIgnoreCase);

    private static void WriteAtomic(string targetPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            IOFile.WriteAllBytes(temporaryPath, bytes);
            IOFile.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

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
        IOFile.Move(sourcePath, targetPath, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (IOFile.Exists(path))
            IOFile.Delete(path);
    }

    private static AssetDependencyData ToData(AssetDependency dependency) => new()
    {
        persistentId = dependency.persistentId,
        stableTypeId = dependency.stableTypeId,
        lastKnownPath = dependency.lastKnownPath
    };

    private static AssetImportDependencyData ToData(AssetImportDependency dependency) => new()
    {
        kind = (int)dependency.kind,
        key = dependency.key,
        fingerprint = dependency.fingerprint
    };

    private static AssetDependency[] GetDirectDependencies(AssetMeta meta)
        => meta.runtimeDependencies.Select(static value => new AssetDependency(
            value.persistentId,
            value.stableTypeId,
            value.lastKnownPath)).ToArray();

    private static AssetDependency FindDescriptor(AssetMeta meta, Guid persistentId)
    {
        AssetDependencyData data = meta.runtimeDependencies.FirstOrDefault(value => value.persistentId == persistentId);
        return data.persistentId == Guid.Empty
            ? default
            : new AssetDependency(data.persistentId, data.stableTypeId, data.lastKnownPath);
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
        AssetDependency[] dependencies);

    private readonly record struct SweepCandidate(
        AssetRecord record,
        WeakReference<AssetObject> reference);

    private readonly record struct FileSnapshot(bool existed, byte[] bytes);
}
