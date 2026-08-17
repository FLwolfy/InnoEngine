using System;
using System.Collections.Generic;

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

    internal abstract AssetImportProduct ImportInternal(AssetImportContext context);
    internal abstract bool TryExportInternal(AssetObject asset, out byte[] sourceBytes);
}

/// <summary>
/// Provides the strongly typed implementation base for an asset importer.
/// </summary>
/// <typeparam name="TAsset">The concrete imported asset type.</typeparam>
public abstract class AssetImporter<TAsset> : AssetImporter where TAsset : AssetObject
{
    /// <inheritdoc/>
    public sealed override Type targetAssetType => typeof(TAsset);

    /// <summary>Imports one source into a managed asset and runtime payload.</summary>
    /// <param name="context">The import transaction context.</param>
    /// <returns>The typed import result.</returns>
    protected abstract AssetImportResult<TAsset> Import(AssetImportContext context);

    /// <summary>Tries to export an asset back into source bytes.</summary>
    /// <param name="asset">The asset to export.</param>
    /// <param name="sourceBytes">The exported source bytes when supported.</param>
    /// <returns><see langword="true"/> when exporting is supported.</returns>
    protected virtual bool TryExport(TAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = [];
        return false;
    }

    internal sealed override AssetImportProduct ImportInternal(AssetImportContext context)
    {
        AssetImportResult<TAsset> result = Import(context);
        return new AssetImportProduct(result.asset, result.runtimePayload);
    }

    internal sealed override bool TryExportInternal(AssetObject asset, out byte[] sourceBytes)
    {
        if (asset is TAsset typed)
            return TryExport(typed, out sourceBytes);
        sourceBytes = [];
        return false;
    }
}

internal readonly struct AssetImportProduct(AssetObject asset, ReadOnlyMemory<byte> runtimePayload)
{
    internal AssetObject asset { get; } = asset ?? throw new ArgumentNullException(nameof(asset));
    internal ReadOnlyMemory<byte> runtimePayload { get; } = runtimePayload;
}
