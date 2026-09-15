using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Declares the immutable protocol identity of an automatically discovered asset importer.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AssetImporterAttribute : Attribute
{
    /// <summary>
    /// Creates importer discovery metadata.
    /// </summary>
    /// <param name="id">Globally stable importer protocol identifier.</param>
    public AssetImporterAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id.Trim();
    }

    /// <summary>
    /// Gets the globally stable importer protocol identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Defines metadata shared by automatically discovered asset importers.
/// </summary>
public abstract class AssetImporter
{
    private string? m_importerId;

    /// <summary>
    /// Gets the stable importer implementation identifier.
    /// </summary>
    public string importerId => m_importerId
        ?? throw new InvalidOperationException(
            $"Asset importer '{GetType().FullName}' has not been bound to discovery metadata.");

    /// <summary>
    /// Gets whether imported assets are deployed or retained only for authoring workflows.
    /// </summary>
    public virtual AssetDeploymentScope deploymentScope => AssetDeploymentScope.Runtime;

    /// <summary>
    /// Gets the concrete asset type produced by this importer.
    /// </summary>
    public abstract Type targetAssetType { get; }

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public abstract IReadOnlyList<string> supportedExtensions { get; }

    /// <summary>
    /// Creates a detached settings value for this importer in the current extension generation.
    /// </summary>
    /// <returns>
    /// A fresh serializable value with a registered stable type identity, or null when this importer has no settings.
    /// Defaults must be deterministic; implementations must not retain the returned instance.
    /// </returns>
    public virtual ISerializable? CreateImportSettings() => null;

    internal abstract ValueTask<AssetImportProduct> ImportInternalAsync(
        AssetImportContext context,
        CancellationToken cancellationToken);
    internal abstract ValueTask<ReadOnlyMemory<byte>?> ExportInternalAsync(
        AssetExportContext context,
        AssetObject asset,
        CancellationToken cancellationToken);

    internal void BindImporterId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (m_importerId is not null && !string.Equals(m_importerId, id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset importer '{GetType().FullName}' cannot be bound to more than one protocol ID.");
        }
        m_importerId = id;
    }
}

/// <summary>
/// Provides the strongly typed implementation base for an asset importer.
/// </summary>
/// <typeparam name="TAsset">
/// The concrete imported asset type.
/// </typeparam>
public abstract class AssetImporter<TAsset> : AssetImporter where TAsset : AssetObject
{
    /// <summary>
    /// Gets the runtime asset type accepted by this importer implementation.
    /// </summary>
    public sealed override Type targetAssetType => typeof(TAsset);

    /// <summary>
    /// Imports one source into a managed asset and named artifact outputs.
    /// </summary>
    /// <param name="context">
    /// The import transaction context.
    /// </param>
    /// <param name="output">
    /// The candidate output writer.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the import.
    /// </param>
    /// <returns>
    /// An operation that completes when the candidate has been fully staged.
    /// </returns>
    protected abstract ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TAsset> output,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exports an asset back into source bytes.
    /// </summary>
    /// <param name="context">
    /// The generation-bound source export services.
    /// </param>
    /// <param name="asset">
    /// The asset to export.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the export.
    /// </param>
    /// <returns>
    /// The source bytes, or <see langword="null"/> when export is unsupported.
    /// </returns>
    protected virtual ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        TAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

    internal sealed override async ValueTask<AssetImportProduct> ImportInternalAsync(
        AssetImportContext context,
        CancellationToken cancellationToken)
    {
        var writer = new AssetImportWriter<TAsset>(context);
        await ImportAsync(context, writer, cancellationToken).ConfigureAwait(false);
        return writer.Complete();
    }

    internal sealed override ValueTask<ReadOnlyMemory<byte>?> ExportInternalAsync(
        AssetExportContext context,
        AssetObject asset,
        CancellationToken cancellationToken)
    {
        if (asset is TAsset typed)
            return ExportAsync(context, typed, cancellationToken);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
    }
}

internal readonly struct AssetImportProduct(
    AssetObject asset,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs,
    IReadOnlyList<string> diagnostics,
    IReadOnlySet<string> authoringOutputs)
{
    internal AssetObject asset { get; } = asset ?? throw new ArgumentNullException(nameof(asset));
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs { get; } = outputs;
    internal IReadOnlyList<string> diagnostics { get; } = diagnostics;
    internal IReadOnlySet<string> authoringOutputs { get; } = authoringOutputs;

    internal ReadOnlyMemory<byte> runtimePayload
        => outputs.TryGetValue("runtime", out ReadOnlyMemory<byte> bytes)
            ? bytes
            : ReadOnlyMemory<byte>.Empty;
}
