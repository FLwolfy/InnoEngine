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

    [ThreadStatic]
    private static AssetLoader? t_activeLoader;

    private readonly SemaphoreSlim m_operationGate = new(1, 1);
    private readonly object m_asyncSync = new();
    private readonly Dictionary<string, Task<AssetObject?>> m_inFlightPathLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Task<AssetObject?>> m_inFlightIdLoads = [];
    private readonly AssetImporterRegistry m_importers = new();
    private readonly AssetBuildProcessorRegistry m_buildProcessors = new();
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
    private readonly AssetSourcePolicy m_sourcePolicy;

    private bool m_disposed;
    private long m_importerRegistryVersion = -1;
    private long m_buildProcessorRegistryVersion = -1;

    /// <summary>Creates an asset loader for one source and Library root pair.</summary>
    /// <param name="assetRoot">The absolute source root.</param>
    /// <param name="libraryRoot">The absolute rebuildable Library root.</param>
    /// <param name="sourcePolicy">The source filtering policy, or <see langword="null"/> for defaults.</param>
    public AssetLoader(
        string assetRoot,
        string libraryRoot,
        AssetSourcePolicy? sourcePolicy = null)
    {
        if (string.IsNullOrWhiteSpace(assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(assetRoot));
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root is required.", nameof(libraryRoot));
        this.assetRoot = Path.GetFullPath(assetRoot);
        this.libraryRoot = Path.GetFullPath(libraryRoot);
        Directory.CreateDirectory(this.assetRoot);
        Directory.CreateDirectory(this.libraryRoot);
        m_sourcePolicy = sourcePolicy ?? AssetSourcePolicy.defaultPolicy;
        m_artifacts = new AssetArtifactStore(this.libraryRoot);
        m_catalog = new AssetCatalogStore(this.libraryRoot);
    }

    /// <summary>Gets the absolute source root.</summary>
    public string assetRoot { get; }

    /// <summary>Gets the absolute rebuildable Library root.</summary>
    public string libraryRoot { get; }

    /// <summary>Gets the derived content-addressed artifact root.</summary>
    public string artifactRoot => m_artifacts.root;

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

    /// <summary>Waits for pending import and build work.</summary>
    public void WaitForIdle()
        => Execute(static () => { });

    /// <summary>Collects unreachable content-addressed artifacts.</summary>
    /// <param name="gracePeriod">The minimum age of an unreachable bundle.</param>
    /// <param name="maximumSizeBytes">The cache size limit, or zero for no limit.</param>
    /// <returns>The number of removed artifact bundles.</returns>
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

    /// <summary>Refreshes extension registries and reimports affected sources when their snapshot changed.</summary>
    /// <returns><see langword="true"/> when the importer registry changed.</returns>
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

    /// <summary>Tries to get a catalog snapshot by source-relative path.</summary>
    public bool TryGetInfo(string relativePath, out AssetInfo? info)
    {
        string normalized = NormalizeRelativePath(relativePath);
        AssetInfo? result = Execute(() => CreateInfo(FindRecordLocked(normalized)));
        info = result;
        return result is not null;
    }

    /// <summary>Tries to get a catalog snapshot by persistent identity.</summary>
    public bool TryGetInfo(Guid persistentId, out AssetInfo? info)
    {
        AssetInfo? result = Execute(() => m_recordsById.TryGetValue(persistentId, out AssetRecord? record)
            ? CreateInfo(record)
            : null);
        info = result;
        return result is not null;
    }

    /// <summary>Tries to resolve a named output from the current artifact bundle.</summary>
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

    /// <summary>Runs the registered aggregate processor for a definition asset.</summary>
    /// <param name="definition">The build definition asset.</param>
    /// <param name="inputs">The immutable input catalog snapshots.</param>
    /// <param name="cancellationToken">Cancellation for the candidate build.</param>
    /// <returns>The content-addressed output bundle key.</returns>
    public ValueTask<AssetArtifactKey> BuildAsync(
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inputs);
        AssetArtifactKey key = Execute(() =>
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
            return m_artifacts.Commit(fingerprint, output.outputs);
        });
        return ValueTask.FromResult(key);
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

        AssetRecord? existingRecord = FindRecordLocked(relativePath);
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
            TypeCacheManager.TryGetStableTypeId(importer.targetAssetType, out Guid stableTypeId))
        {
            record.meta.stableAssetTypeId = stableTypeId;
            record.stableTypeId = stableTypeId;
        }
        record.meta.importStatus = (int)AssetImportStatus.Failed;
        record.meta.diagnostics = [$"{exception.GetType().Name}: {exception.Message}"];
        AddOrReplaceRecordLocked(record);
        WriteSourceMeta(record.meta);
        CommitCatalogLocked();
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
            persistentId);
        AssetImportProduct product = importer
            .ImportInternalAsync(context, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
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

        AssetDependency[] runtimeDependencies = ResolveDeclaredDependenciesLocked(context);
        byte[] state = SerializationManager.Encode(writer => writer.WriteProperties(product.asset));
        AssetRuntimeHost.Initialize(product.asset, relativePath, sourceHash, product.runtimePayload, false, 1);
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
            stableAssetTypeId = stableTypeId,
            assetStateBytes = state,
            runtimeDependencies = runtimeDependencies.Select(ToData).ToArray(),
            importDependencies = context.importDependencies
                .Select(CreateImportDependencyDataLocked)
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
                    build.meta.relativePath,
                    build.meta.sourceHash,
                    replaced.runtimePayload,
                    true,
                    replaced.contentVersion + 1);
                AssetRuntimeHost.Release(replaced);
                IdentityManager.Unregister(replaced);
                existing!.asset = null;
                canonical = null;
                PublishReloaded(replaced);
            }
        }
        if (canonical is not null)
        {
            previousState = SerializationManager.Encode(writer => writer.WriteProperties(canonical));
            previousPayload = canonical.runtimePayload.ToArray();
            previousPath = canonical.sourcePath;
            previousHash = AssetRuntimeHost.GetSourceHash(canonical);
            previousVersion = canonical.contentVersion;
            previousMissing = canonical.isMissing;
            try
            {
                SerializationManager.Decode(build.meta.assetStateBytes, reader =>
                {
                    reader.RestoreProperties(canonical);
                    return 0;
                });
                AssetRuntimeHost.Initialize(
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
        if (!string.IsNullOrWhiteSpace(asset.sourcePath) &&
            !string.Equals(NormalizeRelativePath(asset.sourcePath), relativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset '{asset.sourcePath}' cannot be saved to unrelated path '{relativePath}' without creating a new asset.");
        }
        AssetImporter? importer = m_importers.FindByPath(relativePath);
        if (importer is null || !importer.targetAssetType.IsInstanceOfType(asset))
            return false;
        ReadOnlyMemory<byte>? exported = importer
            .ExportInternalAsync(asset, CancellationToken.None)
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
                AssetRuntimeHost.Release(build.asset);
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
            AssetRuntimeHost.Initialize(asset, relativePath, build.meta.sourceHash, build.payload, false, 1);
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
        return record.meta.isTombstone && record.asset is null
            ? null
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
        byte[] payload = m_artifacts.Read(
            new AssetArtifactKey(record.meta.artifactKey),
            "runtime");
        if (payload.Length == 0 && record.payload.Length > 0)
            payload = record.payload;
        record.payload = payload;
        bool isMissing = record.meta.importStatus == (int)AssetImportStatus.Missing;
        AssetRuntimeHost.Initialize(asset, record.relativePath, record.meta.sourceHash, payload, isMissing, 1);
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
        AssetRuntimeHost.Initialize(missing, lastKnownPath, string.Empty, ReadOnlyMemory<byte>.Empty, true, 0);
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
            bool dependencyStale = dependencyRecord is null ||
                                   IsStale(dependencyRecord, out _);
            if (dependencyStale && !ImportLocked(normalized))
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
        EnsureDirectoryMetadataLocked();
        string[] sourceFiles = Directory.GetFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsSourceIgnored(
                NormalizeRelativePath(Path.GetRelativePath(assetRoot, path)),
                isDirectory: false))
            .ToArray();
        foreach (string sourceFile in sourceFiles)
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(assetRoot, sourceFile));
            AssetImporter? importer = m_importers.FindByPath(relative);
            if (importer is null)
            {
                TrackUnsupportedSourceLocked(relative);
                continue;
            }
            TryAssociateUntrackedRenameLocked(relative, sourceFile);
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
            if (IOFile.Exists(GetSourcePath(record.relativePath)) ||
                Directory.Exists(GetSourcePath(record.relativePath)))
                continue;
            HandleDeletedLocked(record.relativePath);
        }
        CommitCatalogLocked();
        m_importerRegistryVersion = m_importers.snapshotVersion;
        m_buildProcessorRegistryVersion = m_buildProcessors.snapshotVersion;
    }

    private void LoadCatalogLocked()
    {
        AssetMeta[] catalogEntries = m_catalog.Load();
        for (int i = 0; i < catalogEntries.Length; i++)
            MergeCatalogMetaLocked(catalogEntries[i]);

        foreach (string metaPath in Directory.GetFiles(assetRoot, "*" + C_META_POSTFIX, SearchOption.AllDirectories))
        {
            string relativeMeta = NormalizeRelativePath(Path.GetRelativePath(assetRoot, metaPath));
            string relative = relativeMeta[..^C_META_POSTFIX.Length];
            if (Directory.Exists(GetSourcePath(relative)))
                continue;
            try
            {
                AssetSourceMeta sourceMeta = SerializationManager.Deserialize<AssetSourceMeta>(
                    IOFile.ReadAllBytes(metaPath));
                if (sourceMeta.persistentId != Guid.Empty)
                {
                    AssetRecord? existing = FindRecordByIdWithoutLoading(sourceMeta.persistentId);
                    if (existing is not null &&
                        !string.Equals(existing.relativePath, relative, StringComparison.OrdinalIgnoreCase) &&
                        IOFile.Exists(GetSourcePath(existing.relativePath)))
                    {
                        sourceMeta.persistentId = Guid.NewGuid();
                        WriteAtomic(metaPath, SerializationManager.Serialize(sourceMeta));
                    }
                    continue;
                }

            }
            catch
            {
                // A corrupt sidecar remains visible as a catalog diagnostic and is never allowed
                // to terminate the host during source reconciliation.
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
                    newNormalized,
                    record.meta.sourceHash,
                    ReadOnlyMemory<byte>.Empty,
                    true,
                    replaced.contentVersion + 1);
                AssetRuntimeHost.Release(replaced);
                IdentityManager.Unregister(replaced);
                record.asset = null;
                PublishReloaded(replaced);
            }
        }
        else
        {
            record.meta.importStatus = (int)AssetImportStatus.Imported;
            record.meta.diagnostics = [];
            if (record.asset is not null)
                AssetRuntimeHost.UpdateSourcePath(record.asset, newNormalized);
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
                    recordPath,
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
            locations.Add(AssetRuntimeHost.CreateReferenceLocation(
                AssetReferenceKind.AssetDependency,
                dependentId,
                dependent.relativePath,
                "runtimeDependencies"));
        }
        m_recordsById.TryGetValue(id, out AssetRecord? record);
        return AssetRuntimeHost.CreateReferenceInfo(
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
            IdentityManager.Unregister(record.asset);
            record.asset = null;
        }
        m_recordsByPath.Clear();
        m_recordsById.Clear();
        m_runtimeGraph.Clear();
        m_importGraph.Clear();
        m_missingAssets.Clear();
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
                WriteAtomic(metaPath, SerializationManager.Serialize(sourceMeta));
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
        AssetRuntimeHost.Initialize(canonical, sourcePath, sourceHash, payload, isMissing, version);
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

    private AssetInfo? CreateInfo(AssetRecord? record)
    {
        if (record is null)
            return null;
        return new AssetInfo(
            record.persistentId,
            record.relativePath,
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
        => m_catalog.Commit(m_recordsById.Values
            .Concat(m_recordsByPath.Values.Where(static record => record.persistentId == Guid.Empty))
            .Distinct()
            .Select(static record => record.meta)
            .OrderBy(static meta => meta.relativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray());

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
        foreach (string directoryPath in Directory.GetDirectories(assetRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(assetRoot, directoryPath));
            if (IsSourceIgnored(relativePath, isDirectory: true))
                continue;
            string metaPath = GetMetaPath(relativePath);
            AssetSourceMeta sourceMeta;
            if (!TryReadSourceMeta(metaPath, out sourceMeta!))
            {
                sourceMeta = new AssetSourceMeta
                {
                    persistentId = Guid.NewGuid(),
                    sourceKind = (int)AssetSourceKind.Directory
                };
                WriteAtomic(metaPath, SerializationManager.Serialize(sourceMeta));
            }

            AssetRecord? record = FindRecordByIdWithoutLoading(sourceMeta.persistentId);
            if (record is not null &&
                !string.Equals(record.relativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(GetSourcePath(record.relativePath)))
            {
                sourceMeta.persistentId = Guid.NewGuid();
                WriteAtomic(metaPath, SerializationManager.Serialize(sourceMeta));
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
                AssetRuntimeHost.UpdateSourcePath(record.asset, destination);
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
        WriteAtomic(metaPath, SerializationManager.Serialize(sourceMeta));
    }

    private static bool TryReadSourceMeta(string metaPath, out AssetSourceMeta sourceMeta)
    {
        sourceMeta = null!;
        if (!IOFile.Exists(metaPath))
            return false;
        try
        {
            sourceMeta = SerializationManager.Deserialize<AssetSourceMeta>(IOFile.ReadAllBytes(metaPath));
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
           $"{importer.GetType().FullName}";

    private static string CreateBuildFingerprint(
        AssetBuildProcessor processor,
        AssetObject definition,
        IReadOnlyList<AssetInfo> inputs)
    {
        var parts = new List<string>
        {
            "Inno.AssetBuild",
            processor.processorId,
            processor.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D"),
            definition.identity.persistentId.ToString("D")
        };
        foreach (AssetInfo input in inputs.OrderBy(static value => value.persistentId))
        {
            parts.Add(input.persistentId.ToString("D"));
            parts.Add(input.artifactKey.value);
        }
        return string.Join("\n", parts);
    }

    private string GetSourcePath(string relativePath) => Path.Combine(assetRoot, relativePath);
    private string GetMetaPath(string relativePath) => GetSourcePath(relativePath) + C_META_POSTFIX;

    private bool IsSourceIgnored(string relativePath, bool isDirectory)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - (isDirectory ? 0 : 1); i++)
        {
            if (m_sourcePolicy.IsIgnored(segments[i], isDirectory: true))
                return true;
        }
        return m_sourcePolicy.IsIgnored(relativePath, isDirectory);
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
        stableTypeId = dependency.stableTypeId,
        lastKnownPath = dependency.lastKnownPath
    };

    private AssetImportDependencyData CreateImportDependencyDataLocked(AssetImportDependency dependency)
    {
        var result = new AssetImportDependencyData
        {
            kind = (int)dependency.kind,
            key = dependency.key,
            fingerprint = dependency.fingerprint
        };
        result.fingerprint = ComputeImportDependencyFingerprintLocked(ref result, out _);
        return result;
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
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs,
        AssetDependency[] dependencies);

    private readonly record struct SweepCandidate(
        AssetRecord record,
        WeakReference<AssetObject> reference);

    private readonly record struct FileSnapshot(bool existed, byte[] bytes);
}
