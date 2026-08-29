using System;

namespace Inno.Assets.Core;

/// <summary>Describes one committed asset database change.</summary>
public readonly struct AssetChange
{
    /// <summary>Creates an asset change descriptor.</summary>
    /// <param name="kind">Committed change kind.</param>
    /// <param name="persistentId">Persistent identity affected by the change, or empty for a catalog-wide change.</param>
    /// <param name="assetPath">Current isolated path, or an invalid path for a catalog-wide change.</param>
    /// <param name="previousAssetPath">Previous isolated path for a move operation.</param>
    public AssetChange(
        AssetChangeKind kind,
        Guid persistentId,
        AssetPath assetPath,
        AssetPath? previousAssetPath = null)
    {
        this.kind = kind;
        this.persistentId = persistentId;
        this.assetPath = assetPath;
        this.previousAssetPath = previousAssetPath;
    }

    /// <summary>Gets the change kind.</summary>
    public AssetChangeKind kind { get; }

    /// <summary>Gets the persistent identity affected by the change.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the current isolated source path.</summary>
    public AssetPath assetPath { get; }

    /// <summary>Gets the previous isolated path for move operations.</summary>
    public AssetPath? previousAssetPath { get; }
}
