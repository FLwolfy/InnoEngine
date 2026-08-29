using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Provides the narrow host-side bridge used to commit imported state to asset instances.
/// </summary>
/// <remarks>
/// Game code should treat asset instances as immutable. This bridge exists for asset database
/// hosts that need to restore canonical instances without relying on friend assemblies.
/// </remarks>
public static class AssetRuntimeHost
{
    /// <summary>Gets the source fingerprint currently committed to an asset.</summary>
    public static string GetSourceHash(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.sourceHash;
    }

    /// <summary>Commits imported runtime state to a canonical asset instance.</summary>
    public static void Initialize(
        AssetObject asset,
        AssetPath assetPath,
        string sourceHash,
        ReadOnlyMemory<byte> payload,
        bool isMissing,
        long version)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.InitializeRuntimeState(assetPath, sourceHash, payload, isMissing, version);
    }

    /// <summary>Updates the isolated path of a canonical asset without changing its content.</summary>
    /// <param name="asset">Canonical asset to update.</param>
    /// <param name="assetPath">New mount-qualified path.</param>
    public static void UpdateAssetPath(AssetObject asset, AssetPath assetPath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.UpdateAssetPath(assetPath);
    }

    /// <summary>Releases runtime resources owned by a canonical asset.</summary>
    public static void Release(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.ReleaseRuntimeResources();
    }

    /// <summary>Creates one engine-known asset reference location.</summary>
    public static AssetReferenceLocation CreateReferenceLocation(
        AssetReferenceKind kind,
        Guid ownerId,
        string ownerName,
        string propertyPath)
        => new(kind, ownerId, ownerName, propertyPath);

    /// <summary>Creates an immutable asset reference diagnostic snapshot.</summary>
    public static AssetReferenceInfo CreateReferenceInfo(
        Guid persistentId,
        AssetPath assetPath,
        long contentVersion,
        bool isLoaded,
        bool? lastSweepReachability,
        IReadOnlyList<AssetReferenceLocation> references)
        => new(
            persistentId,
            assetPath,
            contentVersion,
            isLoaded,
            lastSweepReachability,
            references);
}
