using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

/// <summary>
/// Collects source data and dependency declarations for one import operation.
/// </summary>
public sealed class AssetImportContext
{
    private readonly List<string> m_runtimeDependencyPaths = [];
    private readonly List<AssetDependency> m_runtimeDependencies = [];
    private readonly List<AssetImportDependency> m_importDependencies = [];

    /// <summary>Creates an asset import context.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="absolutePath">The absolute source path.</param>
    /// <param name="sourceBytes">The raw source bytes.</param>
    /// <param name="sourceHash">The deterministic source hash.</param>
    internal AssetImportContext(
        string relativePath,
        string absolutePath,
        ReadOnlyMemory<byte> sourceBytes,
        string sourceHash)
    {
        this.relativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        this.absolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));
        this.sourceBytes = sourceBytes;
        this.sourceHash = sourceHash ?? throw new ArgumentNullException(nameof(sourceHash));
    }

    /// <summary>Gets the source-relative path.</summary>
    public string relativePath { get; }

    /// <summary>Gets the absolute source path.</summary>
    public string absolutePath { get; }

    /// <summary>Gets the raw source bytes.</summary>
    public ReadOnlyMemory<byte> sourceBytes { get; }

    /// <summary>Gets the deterministic source hash.</summary>
    public string sourceHash { get; }

    /// <summary>Gets the normalized lower-case source extension.</summary>
    public string extension => Path.GetExtension(relativePath).ToLowerInvariant();

    /// <summary>Reads the source bytes as UTF-8 text.</summary>
    /// <returns>The decoded text without an optional byte-order mark.</returns>
    public string ReadUtf8Text()
    {
        string text = Encoding.UTF8.GetString(sourceBytes.Span);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>Declares a direct runtime dependency by source-relative path.</summary>
    /// <param name="relativePath">The dependency source-relative path.</param>
    public void DependsOnAsset(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A runtime dependency path is required.", nameof(relativePath));
        m_runtimeDependencyPaths.Add(Normalize(relativePath));
    }

    /// <summary>Declares a direct runtime dependency by persistent descriptor.</summary>
    /// <param name="dependency">The persistent dependency descriptor.</param>
    public void DependsOnAsset(AssetDependency dependency)
    {
        m_runtimeDependencies.Add(dependency);
    }

    /// <summary>Declares a source file that invalidates this imported asset.</summary>
    /// <param name="relativePath">The source-relative dependency path.</param>
    public void DependsOnSource(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("An import source dependency path is required.", nameof(relativePath));
        m_importDependencies.Add(new AssetImportDependency(
            AssetImportDependencyKind.Source,
            Normalize(relativePath),
            string.Empty));
    }

    /// <summary>Declares an imported artifact that invalidates this imported asset.</summary>
    /// <param name="persistentId">The persistent identity of the artifact owner.</param>
    public void DependsOnArtifact(Guid persistentId)
    {
        if (persistentId == Guid.Empty)
            throw new ArgumentException("An artifact dependency identity is required.", nameof(persistentId));
        m_importDependencies.Add(new AssetImportDependency(
            AssetImportDependencyKind.Artifact,
            persistentId.ToString("D"),
            string.Empty));
    }

    /// <summary>Declares a custom deterministic input that invalidates this asset.</summary>
    /// <param name="key">The input identifier.</param>
    /// <param name="fingerprint">The current deterministic input fingerprint.</param>
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

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
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
