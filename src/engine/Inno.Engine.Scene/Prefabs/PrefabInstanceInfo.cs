using System;

using Inno.Assets.Core;

namespace Inno.Engine.Scene;

/// <summary>
/// Describes the read-only source connection retained by one prefab instance object.
/// </summary>
public sealed class PrefabInstanceInfo
{
    internal PrefabInstanceInfo(
        Guid sourceAssetId,
        Guid sourceObjectId,
        GameObject instanceRoot,
        bool isRoot,
        bool isVariant,
        bool isMissing,
        int overrideCount,
        int orphanedOverrideCount,
        AssetObject sourceAsset)
    {
        this.sourceAssetId = sourceAssetId;
        this.sourceObjectId = sourceObjectId;
        this.instanceRoot = instanceRoot;
        this.isRoot = isRoot;
        this.isVariant = isVariant;
        this.isMissing = isMissing;
        this.overrideCount = overrideCount;
        this.orphanedOverrideCount = orphanedOverrideCount;
        this.sourceAsset = sourceAsset ?? throw new ArgumentNullException(nameof(sourceAsset));
    }

    /// <summary>
    /// Gets the persistent identity of the source prefab asset.
    /// </summary>
    public Guid sourceAssetId { get; }

    /// <summary>
    /// Gets the source-local identity represented by this object.
    /// </summary>
    public Guid sourceObjectId { get; }

    /// <summary>
    /// Gets the root object of this prefab instance connection.
    /// </summary>
    public GameObject instanceRoot { get; }

    /// <summary>
    /// Gets whether this object is the instance connection root.
    /// </summary>
    public bool isRoot { get; }

    /// <summary>
    /// Gets whether the connection originates from a prefab variant.
    /// </summary>
    public bool isVariant { get; }

    /// <summary>
    /// Gets whether the source prefab was unavailable during restoration.
    /// </summary>
    public bool isMissing { get; }

    /// <summary>
    /// Gets the number of retained overrides for this instance connection.
    /// </summary>
    public int overrideCount { get; }

    /// <summary>
    /// Gets the number of retained overrides that no longer match the current source.
    /// </summary>
    public int orphanedOverrideCount { get; }

    internal AssetObject sourceAsset { get; }
}
