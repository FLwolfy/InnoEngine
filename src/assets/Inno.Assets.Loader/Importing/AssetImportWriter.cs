using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

/// <summary>Collects the complete candidate output of one source import.</summary>
/// <typeparam name="TAsset">The imported asset type.</typeparam>
public sealed class AssetImportWriter<TAsset> where TAsset : AssetObject
{
    private readonly AssetImportContext m_context;
    private readonly Dictionary<string, ReadOnlyMemory<byte>> m_outputs =
        new(StringComparer.Ordinal);
    private readonly List<string> m_diagnostics = [];

    internal AssetImportWriter(AssetImportContext context)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Gets the candidate asset assigned by the importer.</summary>
    public TAsset? asset { get; private set; }

    /// <summary>Assigns the managed asset produced by the importer.</summary>
    /// <param name="asset">The imported asset.</param>
    public void SetAsset(TAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (this.asset is not null)
            throw new InvalidOperationException("An importer can assign its asset only once.");
        this.asset = asset;
    }

    /// <summary>Writes one immutable named artifact output.</summary>
    /// <param name="outputName">The stable output name.</param>
    /// <param name="bytes">The output content.</param>
    /// <param name="cancellationToken">Cancellation for the write operation.</param>
    /// <returns>A completed operation after the output has been staged.</returns>
    public ValueTask WriteArtifactAsync(
        string outputName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputName))
            throw new ArgumentException("An artifact output name is required.", nameof(outputName));
        if (!m_outputs.TryAdd(outputName, bytes.ToArray()))
            throw new InvalidOperationException($"Artifact output '{outputName}' was written more than once.");
        return ValueTask.CompletedTask;
    }

    /// <summary>Declares a runtime dependency by source-relative path.</summary>
    /// <param name="relativePath">The dependency path.</param>
    public void DependsOnAsset(string relativePath) => m_context.DependsOnAsset(relativePath);

    /// <summary>Declares a runtime dependency by persistent descriptor.</summary>
    /// <param name="dependency">The dependency descriptor.</param>
    public void DependsOnAsset(AssetDependency dependency) => m_context.DependsOnAsset(dependency);

    /// <summary>Declares a source input that invalidates this import.</summary>
    /// <param name="relativePath">The source-relative input path.</param>
    public void DependsOnSource(string relativePath) => m_context.DependsOnSource(relativePath);

    /// <summary>Declares an asset artifact input that invalidates this import.</summary>
    /// <param name="persistentId">The artifact owner identity.</param>
    public void DependsOnArtifact(Guid persistentId) => m_context.DependsOnArtifact(persistentId);

    /// <summary>Declares a custom deterministic import input.</summary>
    /// <param name="key">The input identifier.</param>
    /// <param name="fingerprint">The deterministic input fingerprint.</param>
    public void DependsOnCustomInput(string key, string fingerprint)
        => m_context.DependsOnCustomInput(key, fingerprint);

    /// <summary>Adds a non-fatal import diagnostic.</summary>
    /// <param name="message">The diagnostic message.</param>
    public void ReportDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A diagnostic message is required.", nameof(message));
        m_diagnostics.Add(message);
    }

    internal AssetImportProduct Complete()
    {
        if (asset is null)
            throw new InvalidOperationException("The importer did not assign an asset.");
        return new AssetImportProduct(asset, m_outputs, m_diagnostics);
    }
}
