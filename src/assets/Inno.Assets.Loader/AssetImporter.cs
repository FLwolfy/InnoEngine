using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

/// <summary>
/// Defines metadata shared by automatically discovered asset importers.
/// </summary>
public abstract class AssetImporter
{
    /// <summary>Gets the stable importer implementation identifier.</summary>
    public virtual string importerId => GetType().FullName ?? GetType().Name;

    /// <summary>Gets the importer version used for cache invalidation.</summary>
    public virtual int version => 1;

    /// <summary>Gets the concrete asset type produced by this importer.</summary>
    public abstract Type targetAssetType { get; }

    /// <summary>Gets the normalized source extensions accepted by this importer.</summary>
    public abstract IReadOnlyList<string> supportedExtensions { get; }

    internal abstract ValueTask<AssetImportProduct> ImportInternalAsync(
        AssetImportContext context,
        CancellationToken cancellationToken);
    internal abstract ValueTask<ReadOnlyMemory<byte>?> ExportInternalAsync(
        AssetObject asset,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides the strongly typed implementation base for an asset importer.
/// </summary>
/// <typeparam name="TAsset">The concrete imported asset type.</typeparam>
public abstract class AssetImporter<TAsset> : AssetImporter where TAsset : AssetObject
{
    /// <inheritdoc/>
    public sealed override Type targetAssetType => typeof(TAsset);

    /// <summary>Imports one source into a managed asset and named artifact outputs.</summary>
    /// <param name="context">The import transaction context.</param>
    /// <param name="output">The candidate output writer.</param>
    /// <param name="cancellationToken">Cancellation for the import.</param>
    /// <returns>An operation that completes when the candidate has been fully staged.</returns>
    protected abstract ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TAsset> output,
        CancellationToken cancellationToken);

    /// <summary>Exports an asset back into source bytes.</summary>
    /// <param name="asset">The asset to export.</param>
    /// <param name="cancellationToken">Cancellation for the export.</param>
    /// <returns>The source bytes, or <see langword="null"/> when export is unsupported.</returns>
    protected virtual ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
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
        AssetObject asset,
        CancellationToken cancellationToken)
    {
        if (asset is TAsset typed)
            return ExportAsync(typed, cancellationToken);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
    }
}

internal readonly struct AssetImportProduct(
    AssetObject asset,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs,
    IReadOnlyList<string> diagnostics)
{
    internal AssetObject asset { get; } = asset ?? throw new ArgumentNullException(nameof(asset));
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs { get; } = outputs;
    internal IReadOnlyList<string> diagnostics { get; } = diagnostics;

    internal ReadOnlyMemory<byte> runtimePayload
        => outputs.TryGetValue("runtime", out ReadOnlyMemory<byte> bytes)
            ? bytes
            : ReadOnlyMemory<byte>.Empty;
}
