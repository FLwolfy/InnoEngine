using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Strongly typed importer output.
/// </summary>
/// <typeparam name="TAsset">Asset type.</typeparam>
public readonly struct AssetImportResult<TAsset> where TAsset : AssetObject
{
    /// <summary>
    /// Imported asset metadata object.
    /// </summary>
    public TAsset asset { get; }

    /// <summary>
    /// Runtime artifact payload bytes.
    /// </summary>
    public byte[] artifactBytes { get; }

    /// <summary>
    /// Referenced dependency paths.
    /// </summary>
    public IReadOnlyList<string> dependencies { get; }

    /// <summary>
    /// Creates a typed import result.
    /// </summary>
    public AssetImportResult(TAsset asset, byte[] artifactBytes, IReadOnlyList<string>? dependencies = null)
    {
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        this.artifactBytes = artifactBytes ?? [];
        this.dependencies = dependencies ?? [];
    }
}
