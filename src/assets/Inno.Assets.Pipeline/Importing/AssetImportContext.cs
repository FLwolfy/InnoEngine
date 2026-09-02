using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scripting.Api;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Collects source data and dependency declarations for one import operation.
/// </summary>
public sealed class AssetImportContext
{
    private readonly List<string> m_runtimeDependencyPaths = [];
    private readonly List<AssetDependency> m_runtimeDependencies = [];
    private readonly List<AssetImportDependency> m_importDependencies = [];
    private readonly Func<string, Type, AssetObject?> m_dependencyResolver;
    private readonly Func<string, ReadOnlyMemory<byte>> m_sourceReader;

    /// <summary>
    /// Creates an asset import context.
    /// </summary>
    /// <param name="relativePath">
    /// The source-relative path.
    /// </param>
    /// <param name="absolutePath">
    /// The absolute source path.
    /// </param>
    /// <param name="sourceBytes">
    /// The raw source bytes.
    /// </param>
    /// <param name="sourceHash">
    /// The deterministic source hash.
    /// </param>
    /// <param name="persistentId">
    /// The persistent identity assigned to the source asset.
    /// </param>
    /// <param name="types">
    /// The immutable-generation owner used to resolve stable extension types.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry bound to the active import generation.
    /// </param>
    /// <param name="references">
    /// The resolver bound to the isolated candidate asset generation.
    /// </param>
    /// <param name="dependencyResolver">
    /// Resolver for typed runtime dependencies.
    /// </param>
    /// <param name="sourceReader">
    /// Reader for controlled source dependencies in the current mount snapshot.
    /// </param>
    internal AssetImportContext(
        string relativePath,
        string absolutePath,
        ReadOnlyMemory<byte> sourceBytes,
        string sourceHash,
        Guid persistentId,
        TypeCatalog types,
        SerializationRegistry serialization,
        IAssetReferenceResolver references,
        Func<string, Type, AssetObject?> dependencyResolver,
        Func<string, ReadOnlyMemory<byte>> sourceReader)
    {
        assetPath = AssetPath.Parse(relativePath ?? throw new ArgumentNullException(nameof(relativePath)));
        this.absolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));
        this.sourceBytes = sourceBytes;
        this.sourceHash = sourceHash ?? throw new ArgumentNullException(nameof(sourceHash));
        this.persistentId = persistentId;
        this.types = types ?? throw new ArgumentNullException(nameof(types));
        this.serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        this.references = references ?? throw new ArgumentNullException(nameof(references));
        services = new AssetSerializationServices(
            types,
            serialization,
            references,
            dependency => m_runtimeDependencies.Add(dependency));
        m_dependencyResolver = dependencyResolver
            ?? throw new ArgumentNullException(nameof(dependencyResolver));
        m_sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
    }

    /// <summary>
    /// Gets the isolated source path.
    /// </summary>
    public AssetPath assetPath { get; }

    /// <summary>
    /// Gets the absolute source path.
    /// </summary>
    public string absolutePath { get; }

    /// <summary>
    /// Gets the raw source bytes.
    /// </summary>
    public ReadOnlyMemory<byte> sourceBytes { get; }

    /// <summary>
    /// Gets the deterministic source hash.
    /// </summary>
    public string sourceHash { get; }

    /// <summary>
    /// Gets the persistent identity assigned to the source asset.
    /// </summary>
    public Guid persistentId { get; }

    /// <summary>
    /// Gets the type catalog bound to the active import generation.
    /// </summary>
    [ScriptingApiIgnore]
    public TypeCatalog types { get; }

    /// <summary>
    /// Gets the serialization registry bound to the active import generation.
    /// </summary>
    [ScriptingApiIgnore]
    public SerializationRegistry serialization { get; }

    /// <summary>
    /// Gets the asset-reference resolver bound to the isolated candidate generation.
    /// </summary>
    [ScriptingApiIgnore]
    public IAssetReferenceResolver references { get; }

    /// <summary>
    /// Gets the narrow structured serialization API bound to this importer candidate.
    /// </summary>
    public AssetSerializationServices services { get; }

    /// <summary>
    /// Gets the normalized lower-case source extension.
    /// </summary>
    public string extension => Path.GetExtension(assetPath.localPath).ToLowerInvariant();

    /// <summary>
    /// Reads the source bytes as UTF-8 text.
    /// </summary>
    /// <returns>
    /// The decoded text without an optional byte-order mark.
    /// </returns>
    public string ReadUtf8Text()
    {
        return DecodeUtf8(sourceBytes.Span);
    }

    /// <summary>
    /// Reads another source from the same candidate mount snapshot and records it as an import dependency.
    /// </summary>
    /// <param name="path">
    /// An isolated path whose mount dependency was declared when it crosses Plugin boundaries.
    /// </param>
    /// <returns>
    /// An immutable source snapshot owned by the import operation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the source crosses an undeclared mount boundary or references the writable Project from a Plugin.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the resolved source does not exist.
    /// </exception>
    public ReadOnlyMemory<byte> ReadSourceBytes(AssetPath path)
    {
        if (!path.isValid)
            throw new ArgumentException("An import source dependency path is required.", nameof(path));
        DependsOnSource(path);
        return m_sourceReader(path.ToString());
    }

    /// <summary>
    /// Reads another source as UTF-8 from the current candidate mount snapshot and records the dependency.
    /// </summary>
    /// <param name="path">
    /// An isolated source path.
    /// </param>
    /// <returns>
    /// Decoded UTF-8 text without an optional byte-order mark.
    /// </returns>
    public string ReadSourceUtf8Text(AssetPath path)
        => DecodeUtf8(ReadSourceBytes(path).Span);

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>
    /// Declares a direct runtime dependency by isolated source path.
    /// </summary>
    /// <param name="path">
    /// The isolated dependency path.
    /// </param>
    public void DependsOnAsset(AssetPath path)
    {
        if (!path.isValid)
            throw new ArgumentException("A runtime dependency path is required.", nameof(path));
        m_runtimeDependencyPaths.Add(path.ToString());
    }

    /// <summary>
    /// Declares a direct runtime dependency by persistent descriptor.
    /// </summary>
    /// <param name="dependency">
    /// The persistent dependency descriptor.
    /// </param>
    public void DependsOnAsset(AssetDependency dependency)
    {
        m_runtimeDependencies.Add(dependency);
    }

    /// <summary>
    /// Resolves and declares a strongly typed runtime asset dependency during import.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Expected asset type.
    /// </typeparam>
    /// <param name="path">
    /// Isolated dependency path.
    /// </param>
    /// <returns>
    /// The currently committed dependency asset.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the dependency cannot be imported or has another type.
    /// </exception>
    public TAsset ResolveDependency<TAsset>(AssetPath path)
        where TAsset : AssetObject
    {
        if (!path.isValid)
        {
            throw new ArgumentException("A runtime dependency path is required.", nameof(path));
        }

        string normalized = path.ToString();
        DependsOnAsset(path);
        AssetObject? resolved = m_dependencyResolver(normalized, typeof(TAsset));
        return resolved as TAsset
            ?? throw new InvalidOperationException(
                $"Asset dependency '{normalized}' cannot be resolved as '{typeof(TAsset).FullName}'.");
    }

    /// <summary>
    /// Declares a source file that invalidates this imported asset.
    /// </summary>
    /// <param name="path">
    /// The isolated dependency path.
    /// </param>
    public void DependsOnSource(AssetPath path)
    {
        if (!path.isValid)
            throw new ArgumentException("An import source dependency path is required.", nameof(path));
        m_importDependencies.Add(new AssetImportDependency(
            AssetImportDependencyKind.Source,
            path.ToString(),
            string.Empty));
    }

    /// <summary>
    /// Declares an imported artifact that invalidates this imported asset.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent identity of the artifact owner.
    /// </param>
    public void DependsOnArtifact(Guid persistentId)
    {
        if (persistentId == Guid.Empty)
            throw new ArgumentException("An artifact dependency identity is required.", nameof(persistentId));
        m_importDependencies.Add(new AssetImportDependency(
            AssetImportDependencyKind.Artifact,
            persistentId.ToString("D"),
            string.Empty));
    }

    /// <summary>
    /// Declares a custom deterministic input that invalidates this asset.
    /// </summary>
    /// <param name="key">
    /// The input identifier.
    /// </param>
    /// <param name="fingerprint">
    /// The current deterministic input fingerprint.
    /// </param>
    public void DependsOnCustomInput(string key, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A custom dependency key is required.", nameof(key));
        m_importDependencies.Add(new AssetImportDependency(
            AssetImportDependencyKind.Custom,
            key,
            fingerprint ?? string.Empty));
    }

    internal IReadOnlyList<string> runtimeDependencyPaths => m_runtimeDependencyPaths;
    internal IReadOnlyList<AssetDependency> runtimeDependencies => m_runtimeDependencies;
    internal IReadOnlyList<AssetImportDependency> importDependencies => m_importDependencies;

}

internal enum AssetImportDependencyKind
{
    Source,
    Artifact,
    Custom
}

internal readonly record struct AssetImportDependency(
    AssetImportDependencyKind kind,
    string key,
    string fingerprint);
