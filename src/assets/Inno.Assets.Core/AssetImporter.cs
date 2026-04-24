using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Generic helper base for implementing <see cref="IAssetImporter"/>.
/// </summary>
/// <typeparam name="TAsset">Target asset type.</typeparam>
public abstract class AssetImporter<TAsset> : IAssetImporter where TAsset : AssetObject
{
    /// <summary>
    /// Stable importer id. Defaults to full type name.
    /// </summary>
    public virtual string importerId => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Importer version used for cache invalidation.
    /// </summary>
    public virtual int version => 1;

    /// <summary>
    /// Target asset type.
    /// </summary>
    public Type targetAssetType => typeof(TAsset);

    /// <summary>
    /// Supported source extensions.
    /// </summary>
    public abstract IReadOnlyList<string> supportedExtensions { get; }

    /// <summary>
    /// Typed import entry point.
    /// </summary>
    /// <param name="context">Import input context.</param>
    /// <returns>Typed import result.</returns>
    public abstract AssetImportResult<TAsset> ImportTyped(in AssetImportContext context);

    /// <summary>
    /// Tries to export asset back to source bytes.
    /// </summary>
    /// <param name="asset">Asset to export.</param>
    /// <param name="sourceBytes">Exported source bytes.</param>
    /// <returns>True when export is supported and succeeded.</returns>
    public virtual bool TryExportTyped(TAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = [];
        return false;
    }

    AssetImportResult<AssetObject> IAssetImporter.Import(in AssetImportContext context)
    {
        AssetImportResult<TAsset> typed = ImportTyped(context);
        return new AssetImportResult<AssetObject>(typed.asset, typed.artifactBytes, typed.dependencies);
    }

    bool IAssetImporter.TryExport(AssetObject asset, out byte[] sourceBytes)
    {
        if (asset is TAsset typed)
            return TryExportTyped(typed, out sourceBytes);

        sourceBytes = [];
        return false;
    }
}
