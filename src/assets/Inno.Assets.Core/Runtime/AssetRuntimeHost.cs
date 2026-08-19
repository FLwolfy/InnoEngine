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
        string relativePath,
        string sourceHash,
        ReadOnlyMemory<byte> payload,
        bool isMissing,
        long version)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.InitializeRuntimeState(relativePath, sourceHash, payload, isMissing, version);
    }

    /// <summary>Updates the source path of a canonical asset without changing its content.</summary>
    public static void UpdateSourcePath(AssetObject asset, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.UpdateSourcePath(relativePath);
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
        string sourcePath,
        long contentVersion,
        bool isLoaded,
        bool? lastSweepReachability,
        IReadOnlyList<AssetReferenceLocation> references)
        => new(
            persistentId,
            sourcePath,
            contentVersion,
            isLoaded,
            lastSweepReachability,
            references);
}
