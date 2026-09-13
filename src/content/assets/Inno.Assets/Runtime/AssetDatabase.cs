using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Core.Execution;
using Inno.Core.Identity;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

namespace Inno.Assets;

/// <summary>
/// Loads canonical runtime assets exclusively from a verified catalog and content-addressed artifact bundles.
/// </summary>
/// <remarks>
/// This database never reads project source files, creates source mounts, runs importers, or writes content.
/// One runtime session owns one database instance and disposes it before releasing its identity services.
/// </remarks>
public sealed partial class AssetDatabase : AssetResidencyProvider,
    IDisposable,
    IAssetLookup,
    IAssetReferenceResolver,
    IAssetPropertyStateResolver,
    IAssetArtifactLookup,
    IAssetResidency
{
    private readonly AssetRuntimeOwner m_runtimeOwner;
    private readonly ArtifactRetention m_artifactRetention = new();
    private readonly object m_sync = new();
    private readonly Dictionary<AssetPath, RuntimeAssetRecord> m_recordsByPath = [];
    private readonly Dictionary<Guid, RuntimeAssetRecord> m_recordsById = [];
    private readonly Dictionary<AssetObject, AssetObject[]> m_dependencyRetention = [];
    private readonly SerializationGeneration m_serialization;
    private readonly SerializationContext m_serializationContext;
    private TypeCacheSnapshot? m_types;
    private readonly IdentityAllocator m_identities;
    private readonly string m_artifactRoot;
    private readonly long m_residencyBudgetBytes;
    private readonly long m_preparationBudgetBytes;
    private long m_accessSequence;
    private long m_residentBytes;
    private bool m_disposed;
    private LifetimeScope? m_retirement;

    /// <inheritdoc />
    public void RestoreProperties<TValue>(Guid stableTypeId, byte[] propertyData, TValue target) where TValue : class, ISerializable
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (m_types!.GetTypeRef(target.GetType()).stableId != stableTypeId)
            throw new InvalidOperationException("The asset property payload has an incompatible stable type identity.");
        m_serialization.Decode(propertyData, reader => { reader.RestoreProperties(target); return true; }, m_serializationContext);
    }

    /// <summary>
    /// Creates a read-only runtime asset database from one materialized content pack.
    /// </summary>
    /// <param name="contentRoot">
    /// The verified runtime content root containing <c>AssetDatabase</c> and <c>Artifacts</c> directories.
    /// </param>
    /// <param name="serialization">
    /// The immutable converter generation pinned by the owning runtime session.
    /// </param>
    /// <param name="identities">
    /// The session-owned allocator used for canonical runtime asset identities.
    /// </param>
    /// <param name="types">
    /// The immutable type generation pinned by this runtime session, without refreshing authoring registries.
    /// </param>
    /// <param name="residencyBudgetBytes">
    /// Maximum unpinned runtime payload bytes retained after lease release.
    /// </param>
    /// <param name="preparationBudgetBytes">
    /// Maximum encoded payload bytes reserved by unfinished cold-load closures, independently of residency.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="contentRoot"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serialization"/> is null.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the deployed catalog or its runtime artifact closure is incomplete or malformed.
    /// </exception>
    public AssetDatabase(
        string contentRoot,
        SerializationGeneration serialization,
        TypeCacheSnapshot types,
        IdentityAllocator identities,
        long residencyBudgetBytes = long.MaxValue,
        long preparationBudgetBytes = 64L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentOutOfRangeException.ThrowIfNegative(residencyBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(preparationBudgetBytes);
        string root = Path.GetFullPath(contentRoot);
        m_serialization = serialization;
        m_runtimeOwner = new(this);
        m_serializationContext = AssetSerializationContext.Create(new PreparedAssetResolver(this));
        m_types = types;
        m_identities = identities;
        m_residencyBudgetBytes = residencyBudgetBytes;
        m_preparationBudgetBytes = preparationBudgetBytes;
        m_artifactRoot = Path.Combine(root, "Artifacts");
        string catalogPath = Path.Combine(root, "AssetDatabase", "Catalog.snapshot");
        if (!File.Exists(catalogPath))
            throw new InvalidDataException($"Runtime asset catalog '{catalogPath}' does not exist.");
        RuntimeAssetCatalog catalog = m_serialization.Deserialize<RuntimeAssetCatalog>(File.ReadAllBytes(catalogPath));
        for (int index = 0; index < catalog.entries.Length; index++)
        {
            RuntimeAssetData data = m_serialization.Deserialize<RuntimeAssetData>(catalog.entries[index]);
            RuntimeAssetRecord record = ValidateRecord(data, index);
            if (!m_recordsByPath.TryAdd(record.path, record))
                throw new InvalidDataException($"Runtime catalog repeats asset path '{record.path}'.");
            if (!m_recordsById.TryAdd(record.persistentId, record))
                throw new InvalidDataException($"Runtime catalog repeats asset identity '{record.persistentId:D}'.");
        }
        foreach (RuntimeAssetRecord record in m_recordsByPath.Values)
        {
            for (int index = 0; index < record.dependencies.Length; index++)
            {
                if (!m_recordsById.ContainsKey(record.dependencies[index].persistentId))
                {
                    throw new InvalidDataException(
                        $"Runtime asset '{record.path}' depends on missing asset " +
                        $"'{record.dependencies[index].persistentId:D}'.");
                }
            }
            ValidateArtifactBundle(record);
        }
    }

    /// <summary>
    /// Loads the canonical asset at one catalog path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required runtime asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <returns>
    /// The canonical asset instance owned by this database.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset exists at the path or its concrete type is incompatible with
    /// <typeparamref name="TAsset"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public TAsset Load<TAsset>(AssetPath path)
        where TAsset : AssetObject
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsByPath.TryGetValue(path, out RuntimeAssetRecord? record))
                throw new InvalidOperationException($"Runtime asset '{path}' is not present in the deployed catalog.");
            return LoadRecord<TAsset>(record, pin: true);
        }
    }

    /// <summary>
    /// Loads the canonical asset with one persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required runtime asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <returns>
    /// The canonical asset instance owned by this database.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset has the identity or its concrete type is incompatible with
    /// <typeparamref name="TAsset"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public TAsset Load<TAsset>(Guid persistentId)
        where TAsset : AssetObject
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record))
                throw new InvalidOperationException($"Runtime asset '{persistentId:D}' is not present in the deployed catalog.");
            return LoadRecord<TAsset>(record, pin: true);
        }
    }

    /// <summary>
    /// Tries to load the canonical asset at one catalog path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required runtime asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the deployed catalog contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public bool TryLoad<TAsset>(AssetPath path, out TAsset? asset)
        where TAsset : AssetObject
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsByPath.TryGetValue(path, out RuntimeAssetRecord? record)
                || !typeof(TAsset).IsAssignableFrom(ResolveType(record)))
            {
                asset = null;
                return false;
            }
            asset = LoadRecord<TAsset>(record, pin: true);
            return true;
        }
    }

    /// <summary>
    /// Tries to load the canonical asset with one persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required runtime asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the deployed catalog contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset)
        where TAsset : AssetObject
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record)
                || !typeof(TAsset).IsAssignableFrom(ResolveType(record)))
            {
                asset = null;
                return false;
            }
            asset = LoadRecord<TAsset>(record, pin: true);
            return true;
        }
    }

    /// <summary>
    /// Gets current materialized asset count, payload bytes, and the configured budget.
    /// </summary>
    public AssetResidencyStatistics residencyStatistics
    {
        get
        {
            lock (m_sync)
            {
                EnsureActive();
                return new AssetResidencyStatistics(
                    m_recordsByPath.Values.Count(static record => record.asset is not null),
                    m_residentBytes,
                    m_residencyBudgetBytes);
            }
        }
    }

    /// <summary>
    /// Acquires a canonical runtime asset by path for an explicit residency lifetime.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required runtime asset contract.
    /// </typeparam>
    /// <param name="path">
    /// Mount-qualified logical asset path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation observed during preparation and before owner-thread materialization.
    /// </param>
    /// <returns>
    /// A lease completed after verified preparation and owner-thread publication.
    /// </returns>
    public ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        AssetPath path,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsByPath.TryGetValue(path, out RuntimeAssetRecord? record))
                throw new InvalidOperationException($"Runtime asset '{path}' is not present in the deployed catalog.");
            return QueueAcquisition<TAsset>(record, cancellationToken);
        }
    }

    /// <summary>
    /// Acquires a canonical runtime asset by persistent identity for an explicit residency lifetime.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Required runtime asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// Persistent asset identity.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation observed during preparation and before owner-thread materialization.
    /// </param>
    /// <returns>
    /// A lease completed after verified preparation and owner-thread publication.
    /// </returns>
    public ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
        Guid persistentId,
        CancellationToken cancellationToken = default)
        where TAsset : AssetObject
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record))
            {
                throw new InvalidOperationException(
                    $"Runtime asset '{persistentId:D}' is not present in the deployed catalog.");
            }
            return QueueAcquisition<TAsset>(record, cancellationToken);
        }
    }

    /// <summary>
    /// Gets the direct persistent dependencies declared by one loaded or cataloged asset.
    /// </summary>
    /// <param name="asset">
    /// The canonical asset whose deployed dependency descriptors are requested.
    /// </param>
    /// <returns>
    /// A deterministic immutable snapshot of direct dependencies.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="asset"/> is not owned by this database.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public IReadOnlyList<AssetDependency> GetDependencies(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsById.TryGetValue(asset.identity.persistentId, out RuntimeAssetRecord? record)
                || !ReferenceEquals(record.asset, asset))
            {
                throw new InvalidOperationException("Only an asset owned by this database has runtime dependencies.");
            }
            return record.dependencies.ToArray();
        }
    }

    /// <summary>
    /// Tries to resolve one named immutable artifact output by persistent asset identity.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent identity of the artifact owner.
    /// </param>
    /// <param name="outputName">
    /// The exact artifact output name.
    /// </param>
    /// <param name="artifact">
    /// Receives verified output metadata and its absolute immutable path when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the deployed bundle contains the requested output; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this database has been disposed.
    /// </exception>
    public bool TryGetArtifact(
        Guid persistentId,
        string outputName,
        out AssetArtifactInfo? artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        lock (m_sync)
        {
            EnsureActive();
            artifact = null;
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record))
                return false;
            RuntimeArtifactOutput output = ReadArtifactManifest(record).outputs.FirstOrDefault(candidate =>
                string.Equals(candidate.name, outputName, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(output.name))
                return false;
            string artifactPath = GetVerifiedOutputPath(record, output);
            artifact = new AssetArtifactInfo(
                new AssetArtifactKey(record.artifactKey),
                output.name,
                artifactPath,
                output.contentHash,
                output.length);
            return true;
        }
    }

    /// <summary>
    /// Acquires one verified immutable artifact output for an explicit lifetime.
    /// </summary>
    /// <param name="persistentId">
    /// Persistent identity of the artifact owner.
    /// </param>
    /// <param name="outputName">
    /// Stable artifact output name.
    /// </param>
    /// <returns>
    /// A lease over the verified deployed artifact.
    /// </returns>
    public ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
    {
        if (!TryGetArtifact(persistentId, outputName, out AssetArtifactInfo? artifact) || artifact is null)
        {
            throw new InvalidOperationException(
                $"Runtime asset '{persistentId:D}' has no verified artifact output '{outputName}'.");
        }
        return m_artifactRetention.Retain(artifact);
    }

    /// <summary>
    /// Evicts least-recently-used unpinned assets until the configured payload budget is met.
    /// </summary>
    /// <returns>
    /// The number of canonical assets released by this trim operation.
    /// </returns>
    public int TrimToBudget()
    {
        lock (m_sync)
        {
            EnsureActive();
            return TrimToBudgetLocked();
        }
    }

    /// <summary>
    /// Stops publication and releases canonical assets before dependency edges and the payload read gate.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// An unload hook still owns active work. Retain this database and retry at the session retirement safe point.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Completed releases failed after all quiescent resources were attempted.
    /// </exception>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            if (m_retirement is null)
            {
                m_retirement = new LifetimeScope();
                m_retirement.Own(m_payloadReadSlots);
                foreach (RuntimeAssetRecord record in m_recordsById.Values)
                    if (record.asset is not null)
                        m_retirement.Own(new RuntimeAssetRetirement(this, record));
            }
            CancelPendingLoads();
            try { m_retirement.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch
            {
                FinishRetirement();
                throw;
            }
            FinishRetirement();
        }
    }

    private void FinishRetirement()
    {
        m_dependencyRetention.Clear();
        m_recordsByPath.Clear();
        m_recordsById.Clear();
        m_residentBytes = 0;
        m_types = null;
        m_disposed = true;
    }

    private TAsset LoadRecord<TAsset>(RuntimeAssetRecord record, bool pin,
        IReadOnlyDictionary<Guid, byte[]>? preparedPayloads = null)
        where TAsset : AssetObject
    {
        if (record.isRetiring)
            Evict(record);
        Type actualType = ResolveType(record);
        if (!typeof(TAsset).IsAssignableFrom(actualType))
        {
            throw new InvalidOperationException(
                $"Runtime asset '{record.path}' has type '{actualType.FullName}', not '{typeof(TAsset).FullName}'.");
        }
        record.isPinned |= pin;
        record.lastAccess = ++m_accessSequence;
        if (record.asset is TAsset loaded)
            return loaded;

        var created = new List<RuntimeAssetRecord>();
        try
        {
            PrepareShells(record, created);
            for (int index = 0; index < created.Count; index++)
                Hydrate(created[index], preparedPayloads);
            for (int index = 0; index < created.Count; index++)
                RetainDependencies(created[index]);
            return (TAsset)record.asset!;
        }
        catch
        {
            for (int index = created.Count - 1; index >= 0; index--)
            {
                RuntimeAssetRecord createdRecord = created[index];
                if (createdRecord.asset is null)
                    continue;
                m_dependencyRetention.Remove(createdRecord.asset);
                m_residentBytes -= createdRecord.runtimeBytes;
                createdRecord.runtimeBytes = 0;
                m_runtimeOwner.Release(createdRecord.asset);
                _ = m_identities.Unregister(createdRecord.asset);
                createdRecord.asset = null;
            }
            throw;
        }
    }

    private void PrepareShells(RuntimeAssetRecord record, ICollection<RuntimeAssetRecord> created)
    {
        if (record.asset is not null)
            return;
        Type type = ResolveType(record);
        AssetObject asset;
        try
        {
            asset = (AssetObject)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException("Activator returned null."));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Runtime asset type '{type.FullName}' requires a parameterless constructor.",
                exception);
        }
        m_identities.InitializePersistentIdentity(asset, record.persistentId);
        m_identities.Register(asset, record.persistentId);
        record.asset = asset;
        created.Add(record);
        for (int index = 0; index < record.dependencies.Length; index++)
            PrepareShells(m_recordsById[record.dependencies[index].persistentId], created);
    }

    private void Hydrate(RuntimeAssetRecord record, IReadOnlyDictionary<Guid, byte[]>? preparedPayloads)
    {
        AssetObject asset = record.asset!;
        m_serialization.Decode(record.assetState, reader =>
        {
            reader.RestoreProperties(asset);
            return true;
        }, m_serializationContext);
        byte[] payload;
        if (preparedPayloads is not null)
            payload = preparedPayloads[record.persistentId];
        else
        {
            RuntimeArtifactOutput output = ReadArtifactManifest(record).outputs.Single(candidate =>
                string.Equals(candidate.name, "runtime", StringComparison.Ordinal));
            payload = File.ReadAllBytes(GetVerifiedOutputPath(record, output));
        }
        m_runtimeOwner.Initialize(asset, record.path, record.sourceHash, payload, isMissing: false, version: 1);
        record.runtimeBytes = payload.LongLength;
        m_residentBytes += record.runtimeBytes;
    }

    private void RetainDependencies(RuntimeAssetRecord record)
    {
        AssetObject[] dependencies = record.dependencies
            .Select(dependency => m_recordsById[dependency.persistentId].asset
                ?? throw new InvalidOperationException("A runtime dependency was not prepared."))
            .ToArray();
        m_dependencyRetention[record.asset!] = dependencies;
    }

    private void ReleaseLease(RuntimeAssetRecord record, ref bool released)
    {
        lock (m_sync)
        {
            if (m_disposed || m_retirement is not null)
                return;
            if (!released)
            {
                if (record.leaseCount <= 0)
                    throw new InvalidOperationException("Runtime asset residency has no matching lease to release.");
                record.leaseCount--;
                released = true;
                record.lastAccess = ++m_accessSequence;
            }
            _ = TrimToBudgetLocked();
        }
    }

    private int TrimToBudgetLocked()
    {
        int released = 0;
        while (m_residentBytes > m_residencyBudgetBytes)
        {
            RuntimeAssetRecord? candidate = m_recordsByPath.Values
                .Where(CanEvict)
                .OrderBy(static record => record.lastAccess)
                .ThenBy(static record => record.path.ToString(), StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null)
                break;
            Evict(candidate);
            released++;
        }
        return released;
    }

    private bool CanEvict(RuntimeAssetRecord record)
    {
        AssetObject? asset = record.asset;
        if (asset is null || record.isPinned || record.leaseCount != 0)
            return false;
        foreach ((AssetObject owner, AssetObject[] dependencies) in m_dependencyRetention)
        {
            if (!ReferenceEquals(owner, asset) && dependencies.Contains(asset, ReferenceEqualityComparer.Instance))
                return false;
        }
        return true;
    }

    private void Evict(RuntimeAssetRecord record)
    {
        AssetObject asset = record.asset
            ?? throw new InvalidOperationException("Only a materialized asset can be evicted.");
        record.isRetiring = true;
        List<Exception> failures = [];
        try { m_runtimeOwner.Release(asset); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure) { failures.Add(failure); }
        try { m_identities.Unregister(asset); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure) { failures.Add(failure); }
        m_dependencyRetention.Remove(asset);
        record.asset = null;
        m_residentBytes -= record.runtimeBytes;
        record.runtimeBytes = 0;
        record.isRetiring = false;
        if (failures.Count > 0)
            throw new AggregateException("Runtime asset retirement failed after releasing its registration.", failures);
    }

    AssetObject IAssetReferenceResolver.Resolve(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
        => ResolveReference(persistentId, stableTypeId, expectedType, propertyPath, requirePrepared: false);

    private AssetObject ResolveReference(Guid persistentId, Guid stableTypeId, Type expectedType,
        string propertyPath, bool requirePrepared)
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record) ||
                (requirePrepared && record.asset is null))
                throw new InvalidDataException(
                    $"Asset reference '{persistentId:D}' at '{propertyPath}' is outside the deployed runtime closure.");
            if (stableTypeId != Guid.Empty && record.stableTypeId != stableTypeId)
                throw new InvalidDataException(
                    $"Asset reference '{persistentId:D}' at '{propertyPath}' has a mismatched stable type identity.");
            if (!expectedType.IsAssignableFrom(ResolveType(record)))
                throw new InvalidDataException(
                    $"Asset reference '{persistentId:D}' at '{propertyPath}' is incompatible with '{expectedType.FullName}'.");
            return requirePrepared ? record.asset! : LoadRecord<AssetObject>(record, pin: true);
        }
    }

    private RuntimeAssetRecord ValidateRecord(RuntimeAssetData data, int index)
    {
        if (data.persistentId == Guid.Empty)
            throw new InvalidDataException($"Runtime catalog entry {index} has no persistent identity.");
        if (data.stableAssetTypeId == Guid.Empty)
            throw new InvalidDataException($"Runtime catalog entry {index} has no stable asset type identity.");
        if (data.isDirectory || data.isTombstone)
            throw new InvalidDataException($"Runtime catalog entry {index} is not a live deployable asset.");
        if (data.deploymentScope != 0 || data.importStatus != (int)AssetImportStatus.Imported)
            throw new InvalidDataException($"Runtime catalog entry {index} is not a successfully imported runtime asset.");
        if (data.assetStateBytes.Length == 0)
            throw new InvalidDataException($"Runtime catalog entry {index} has no serialized asset state.");
        if (data.artifactKey.Length != 64 || data.artifactKey.Any(static character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Runtime catalog entry {index} has an invalid artifact identity.");
        AssetPath path;
        try
        {
            path = AssetPath.Parse(data.relativePath);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Runtime catalog entry {index} has an invalid logical path.", exception);
        }
        AssetDependency[] dependencies = data.runtimeDependencies.Select(dependency => new AssetDependency(
            dependency.persistentId,
            new TypeRef(dependency.stableTypeId),
            dependency.lastKnownPath ?? string.Empty)).ToArray();
        return new RuntimeAssetRecord(
            data.persistentId,
            path,
            data.sourceHash,
            data.stableAssetTypeId,
            data.artifactKey.ToUpperInvariant(),
            data.assetStateBytes,
            dependencies);
    }

    private Type ResolveType(RuntimeAssetRecord record)
    {
        Type type = new TypeRef(record.stableTypeId).Resolve(m_types!);
        if (!typeof(AssetObject).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidDataException(
                $"Runtime asset '{record.path}' resolves to invalid type '{type.FullName}'.");
        }
        return type;
    }

    private void ValidateArtifactBundle(RuntimeAssetRecord record)
    {
        RuntimeArtifactManifest manifest = ReadArtifactManifest(record);
        if (!string.Equals(manifest.key, record.artifactKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Runtime artifact bundle '{record.artifactKey}' has a mismatched manifest identity.");
        string[] names = manifest.outputs.Select(static output => output.name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || !names.Contains("asset-state", StringComparer.Ordinal)
            || !names.Contains("runtime", StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Runtime artifact bundle '{record.artifactKey}' does not contain one complete asset-state/runtime pair.");
        }
        foreach (RuntimeArtifactOutput output in manifest.outputs)
            _ = GetVerifiedOutputPath(record, output);
    }

    private RuntimeArtifactManifest ReadArtifactManifest(RuntimeAssetRecord record)
    {
        string manifestPath = Path.Combine(GetBundleRoot(record.artifactKey), "manifest");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Runtime artifact bundle '{record.artifactKey}' has no manifest.");
        return m_serialization.Deserialize<RuntimeArtifactManifest>(File.ReadAllBytes(manifestPath));
    }

    private string GetVerifiedOutputPath(RuntimeAssetRecord record, RuntimeArtifactOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.name) || string.IsNullOrWhiteSpace(output.fileName)
            || Path.GetFileName(output.fileName) != output.fileName)
        {
            throw new InvalidDataException($"Runtime artifact bundle '{record.artifactKey}' contains an invalid output path.");
        }
        string path = Path.Combine(GetBundleRoot(record.artifactKey), "outputs", output.fileName);
        if (!File.Exists(path))
            throw new InvalidDataException($"Runtime artifact output '{record.artifactKey}/{output.name}' is missing.");
        var info = new FileInfo(path);
        if (info.Length != output.length)
            throw new InvalidDataException($"Runtime artifact output '{record.artifactKey}/{output.name}' has an invalid length.");
        using FileStream stream = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, output.contentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Runtime artifact output '{record.artifactKey}/{output.name}' failed hash verification.");
        return path;
    }

    private string GetBundleRoot(string key)
        => Path.Combine(m_artifactRoot, key[..2].ToLowerInvariant(), key[2..4].ToLowerInvariant(), key);

    private void EnsureActive()
        => ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);

    private sealed class PreparedAssetResolver(AssetDatabase owner) : IAssetReferenceResolver
    {
        /// <summary>
        /// Resolves only shells already prepared from the importing asset's declared runtime closure.
        /// </summary>
        /// <param name="persistentId">
        /// The preserved asset identity.
        /// </param>
        /// <param name="stableTypeId">
        /// The expected stable asset type.
        /// </param>
        /// <param name="lastKnownPath">
        /// The diagnostic source hint, never an identity fallback.
        /// </param>
        /// <param name="expectedType">
        /// The required assignable runtime type.
        /// </param>
        /// <param name="propertyPath">
        /// The referencing serialized property path.
        /// </param>
        /// <returns>
        /// The already prepared canonical shell, or an exception for an undeclared reference.
        /// </returns>
        public AssetObject Resolve(Guid persistentId, Guid stableTypeId, string lastKnownPath,
            Type expectedType, string propertyPath)
            => owner.ResolveReference(persistentId, stableTypeId, expectedType, propertyPath, requirePrepared: true);
    }

    private sealed class RuntimeAssetRetirement(AssetDatabase owner, RuntimeAssetRecord record) : IDisposable
    {
        /// <summary>
        /// Retains a canonical runtime record until its unload hook finishes.
        /// </summary>
        public void Dispose()
        {
            if (record.asset is not null)
                owner.Evict(record);
        }
    }

    private sealed class RuntimeAssetRecord(
        Guid persistentId,
        AssetPath path,
        string sourceHash,
        Guid stableTypeId,
        string artifactKey,
        byte[] assetState,
        AssetDependency[] dependencies)
    {
        internal Guid persistentId { get; } = persistentId;
        internal AssetPath path { get; } = path;
        internal string sourceHash { get; } = sourceHash;
        internal Guid stableTypeId { get; } = stableTypeId;
        internal string artifactKey { get; } = artifactKey;
        internal byte[] assetState { get; } = assetState;
        internal AssetDependency[] dependencies { get; } = dependencies;
        internal AssetObject? asset { get; set; }
        internal bool isPinned { get; set; }
        internal bool isRetiring { get; set; }
        internal int leaseCount { get; set; }
        internal long lastAccess { get; set; }
        internal long runtimeBytes { get; set; }
    }

    private sealed class RuntimeAssetCatalog : ISerializable
    {
        [SerializableProperty]
        internal long revision { get; set; }

        [SerializableProperty]
        internal byte[][] entries { get; set; } = [];
    }

    private sealed class RuntimeAssetData : ISerializable
    {
        [SerializableProperty] internal Guid persistentId { get; set; }
        [SerializableProperty] internal string relativePath { get; set; } = string.Empty;
        [SerializableProperty] internal string sourceHash { get; set; } = string.Empty;
        [SerializableProperty] internal long sourceLength { get; set; }
        [SerializableProperty] internal long sourceLastWriteUtcTicks { get; set; }
        [SerializableProperty] internal long sourceCreationTimeUtcTicks { get; set; }
        [SerializableProperty] internal string importerId { get; set; } = string.Empty;
        [SerializableProperty] internal int deploymentScope { get; set; }
        [SerializableProperty] internal Guid stableAssetTypeId { get; set; }
        [SerializableProperty] internal byte[] assetStateBytes { get; set; } = [];
        [SerializableProperty] internal RuntimeDependencyData[] runtimeDependencies { get; set; } = [];
        [SerializableProperty] internal RuntimeImportDependencyData[] importDependencies { get; set; } = [];
        [SerializableProperty] internal int importStatus { get; set; }
        [SerializableProperty] internal string importerImplementationFingerprint { get; set; } = string.Empty;
        [SerializableProperty] internal string artifactKey { get; set; } = string.Empty;
        [SerializableProperty] internal string lastSuccessfulArtifactKey { get; set; } = string.Empty;
        [SerializableProperty] internal string[] diagnostics { get; set; } = [];
        [SerializableProperty] internal bool isDirectory { get; set; }
        [SerializableProperty] internal bool isTombstone { get; set; }
    }

    private struct RuntimeDependencyData
    {
        [SerializableProperty] internal Guid persistentId { get; set; }
        [SerializableProperty] internal Guid stableTypeId { get; set; }
        [SerializableProperty] internal string lastKnownPath { get; set; }
    }

    private struct RuntimeImportDependencyData
    {
        [SerializableProperty] internal int kind { get; set; }
        [SerializableProperty] internal string key { get; set; }
        [SerializableProperty] internal string fingerprint { get; set; }
        [SerializableProperty] internal bool sourceStampValid { get; set; }
        [SerializableProperty] internal long sourceLength { get; set; }
        [SerializableProperty] internal long sourceLastWriteUtcTicks { get; set; }
        [SerializableProperty] internal long sourceCreationTimeUtcTicks { get; set; }
    }

    private sealed class RuntimeArtifactManifest : ISerializable
    {
        [SerializableProperty] internal string key { get; set; } = string.Empty;
        [SerializableProperty] internal RuntimeArtifactOutput[] outputs { get; set; } = [];
    }

    private struct RuntimeArtifactOutput
    {
        [SerializableProperty] internal string name { get; set; }
        [SerializableProperty] internal string fileName { get; set; }
        [SerializableProperty] internal string contentHash { get; set; }
        [SerializableProperty] internal long length { get; set; }
    }
}
