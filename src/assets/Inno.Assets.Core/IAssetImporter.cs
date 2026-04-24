using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Contract for source-to-asset importers used by <c>AssetManager</c>.
/// </summary>
public interface IAssetImporter
{
    /// <summary>
    /// Stable identifier for this importer implementation.
    /// </summary>
    string importerId { get; }
    /// <summary>
    /// Importer version used for cache invalidation.
    /// </summary>
    int version { get; }
    /// <summary>
    /// Target asset runtime type produced by this importer.
    /// </summary>
    Type targetAssetType { get; }
    /// <summary>
    /// Supported source file extensions.
    /// </summary>
    IReadOnlyList<string> supportedExtensions { get; }

    /// <summary>
    /// Imports source bytes into asset object + runtime artifact.
    /// </summary>
    /// <param name="context">Import input context.</param>
    /// <returns>Import output.</returns>
    AssetImportResult Import(in AssetImportContext context);

    /// <summary>
    /// Tries to export asset object back to source bytes.
    /// </summary>
    /// <param name="asset">Asset object to export.</param>
    /// <param name="sourceBytes">Exported source bytes.</param>
    /// <returns>True when export is supported and succeeded.</returns>
    bool TryExport(AssetObject asset, out byte[] sourceBytes);
}
