using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Inno.Assets;
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
public sealed class AssetDatabase : IDisposable, IAssetLookup, IAssetReferenceResolver
{
    private readonly object m_sync = new();
    private readonly Dictionary<AssetPath, RuntimeAssetRecord> m_recordsByPath = [];
    private readonly Dictionary<Guid, RuntimeAssetRecord> m_recordsById = [];
    private readonly Dictionary<AssetObject, AssetObject[]> m_dependencyRetention = [];
    private readonly SerializationGeneration m_serialization;
    private readonly SerializationContext m_serializationContext;
    private readonly TypeCatalog m_types;
    private readonly IdentityAllocator m_identities;
    private readonly string m_artifactRoot;
    private bool m_disposed;

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
    /// The immutable-generation owner used to resolve stable runtime asset type identities.
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
        TypeCatalog types,
        IdentityAllocator identities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(identities);
        string root = Path.GetFullPath(contentRoot);
        m_serialization = serialization;
        m_serializationContext = SerializationContext.empty.With<IAssetReferenceResolver>(this);
        m_types = types;
        m_identities = identities;
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
            return LoadRecord<TAsset>(record);
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
            return LoadRecord<TAsset>(record);
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
            asset = LoadRecord<TAsset>(record);
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
            asset = LoadRecord<TAsset>(record);
            return true;
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
    /// <param name="artifactPath">
    /// Receives the verified absolute artifact file path when successful.
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
        out string? artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        lock (m_sync)
        {
            EnsureActive();
            artifactPath = null;
            if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record))
                return false;
            RuntimeArtifactOutput output = ReadArtifactManifest(record).outputs.FirstOrDefault(candidate =>
                string.Equals(candidate.name, outputName, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(output.name))
                return false;
            artifactPath = GetVerifiedOutputPath(record, output);
            return true;
        }
    }

    /// <summary>
    /// Releases every canonical asset and dependency edge owned by this runtime database.
    /// </summary>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_dependencyRetention.Clear();
            foreach (RuntimeAssetRecord record in m_recordsByPath.Values.Reverse())
            {
                if (record.asset is null)
                    continue;
                AssetRuntimeHost.Release(record.asset);
                _ = m_identities.Unregister(record.asset);
                record.asset = null;
            }
            m_recordsByPath.Clear();
            m_recordsById.Clear();
        }
    }

    private TAsset LoadRecord<TAsset>(RuntimeAssetRecord record)
        where TAsset : AssetObject
    {
        Type actualType = ResolveType(record);
        if (!typeof(TAsset).IsAssignableFrom(actualType))
        {
            throw new InvalidOperationException(
                $"Runtime asset '{record.path}' has type '{actualType.FullName}', not '{typeof(TAsset).FullName}'.");
        }
        if (record.asset is TAsset loaded)
            return loaded;

        var created = new List<RuntimeAssetRecord>();
        try
        {
            PrepareShells(record, created);
            for (int index = 0; index < created.Count; index++)
                Hydrate(created[index]);
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
                AssetRuntimeHost.Release(createdRecord.asset);
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

    private void Hydrate(RuntimeAssetRecord record)
    {
        AssetObject asset = record.asset!;
        m_serialization.Decode(record.assetState, reader =>
        {
            reader.RestoreProperties(asset);
            return true;
        }, m_serializationContext);
        RuntimeArtifactOutput output = ReadArtifactManifest(record).outputs.Single(candidate =>
            string.Equals(candidate.name, "runtime", StringComparison.Ordinal));
        byte[] payload = File.ReadAllBytes(GetVerifiedOutputPath(record, output));
        AssetRuntimeHost.Initialize(asset, record.path, record.sourceHash, payload, isMissing: false, version: 1);
    }

    private void RetainDependencies(RuntimeAssetRecord record)
    {
        AssetObject[] dependencies = record.dependencies
            .Select(dependency => m_recordsById[dependency.persistentId].asset
                ?? throw new InvalidOperationException("A runtime dependency was not prepared."))
            .ToArray();
        m_dependencyRetention[record.asset!] = dependencies;
    }

    AssetObject IAssetReferenceResolver.Resolve(
        Guid persistentId,
        Guid stableTypeId,
        string lastKnownPath,
        Type expectedType,
        string propertyPath)
    {
        if (!m_recordsById.TryGetValue(persistentId, out RuntimeAssetRecord? record) || record.asset is null)
        {
            throw new InvalidDataException(
                $"Asset reference '{persistentId:D}' at '{propertyPath}' is outside the deployed runtime closure.");
        }
        if (stableTypeId != Guid.Empty && record.stableTypeId != stableTypeId)
        {
            throw new InvalidDataException(
                $"Asset reference '{persistentId:D}' at '{propertyPath}' has a mismatched stable type identity.");
        }
        if (!expectedType.IsInstanceOfType(record.asset))
        {
            throw new InvalidDataException(
                $"Asset reference '{persistentId:D}' at '{propertyPath}' is incompatible with '{expectedType.FullName}'.");
        }
        return record.asset;
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
        Type type = m_types.Resolve(new TypeRef(record.stableTypeId));
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
        => ObjectDisposedException.ThrowIf(m_disposed, this);

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
