using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Untyped importer output.
/// </summary>
public readonly struct AssetImportResult
{
    /// <summary>
    /// Imported asset metadata object.
    /// </summary>
    public AssetObject asset { get; }
    /// <summary>
    /// Runtime artifact payload bytes.
    /// </summary>
    public byte[] artifactBytes { get; }
    /// <summary>
    /// Referenced dependency paths.
    /// </summary>
    public IReadOnlyList<string> dependencies { get; }

    /// <summary>
    /// Creates an untyped import result.
    /// </summary>
    /// <param name="asset">Imported asset object.</param>
    /// <param name="artifactBytes">Runtime artifact bytes.</param>
    /// <param name="dependencies">Dependency path list.</param>
    public AssetImportResult(AssetObject asset, byte[] artifactBytes, IReadOnlyList<string>? dependencies = null)
    {
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        this.artifactBytes = artifactBytes ?? [];
        this.dependencies = dependencies ?? [];
    }
}

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
    /// <param name="asset">Imported asset object.</param>
    /// <param name="artifactBytes">Runtime artifact bytes.</param>
    /// <param name="dependencies">Dependency path list.</param>
    public AssetImportResult(TAsset asset, byte[] artifactBytes, IReadOnlyList<string>? dependencies = null)
    {
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        this.artifactBytes = artifactBytes ?? [];
        this.dependencies = dependencies ?? [];
    }

    /// <summary>
    /// Converts to untyped result representation.
    /// </summary>
    public AssetImportResult ToUntyped()
        => new(asset, artifactBytes, dependencies);
}
