using System;
using System.Collections.Generic;

namespace Inno.Assets;

/// <summary>
/// Provides the narrow host-side bridge used to commit imported state to asset instances.
/// </summary>
/// <remarks>
/// Game code should treat asset instances as immutable. This bridge exists for asset database
/// hosts that need to restore canonical instances without relying on friend assemblies.
/// </remarks>
public static class AssetRuntimeHost
{
    /// <summary>
    /// Gets the source fingerprint currently committed to an asset.
    /// </summary>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
    public static string GetSourceHash(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.sourceHash;
    }

    /// <summary>
    /// Commits imported runtime state to a canonical asset instance.
    /// </summary>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    /// <param name="assetPath">
    /// The asset path consumed by initialize; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sourceHash">
    /// The source hash text validated by the initialize operation.
    /// </param>
    /// <param name="payload">
    /// The payload consumed by initialize; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="isMissing">
    /// Whether is missing behavior is enabled while initialize executes.
    /// </param>
    /// <param name="version">
    /// The version consumed by initialize; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
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

    /// <summary>
    /// Updates the isolated path of a canonical asset without changing its content.
    /// </summary>
    /// <param name="asset">
    /// Canonical asset to update.
    /// </param>
    /// <param name="assetPath">
    /// New mount-qualified path.
    /// </param>
    public static void UpdateAssetPath(AssetObject asset, AssetPath assetPath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.UpdateAssetPath(assetPath);
    }

    /// <summary>
    /// Releases runtime resources owned by a canonical asset.
    /// </summary>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    public static void Release(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.ReleaseRuntimeResources();
    }

    /// <summary>
    /// Creates one engine-known asset reference location.
    /// </summary>
    /// <param name="kind">
    /// The kind consumed by create reference location; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="ownerId">
    /// The owner id consumed by create reference location; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="ownerName">
    /// The owner name text validated by the create reference location operation.
    /// </param>
    /// <param name="propertyPath">
    /// The property path text validated by the create reference location operation.
    /// </param>
    /// <returns>
    /// The validated asset reference location that represents the completed operation.
    /// </returns>
    public static AssetReferenceLocation CreateReferenceLocation(
        AssetReferenceKind kind,
        Guid ownerId,
        string ownerName,
        string propertyPath)
        => new(kind, ownerId, ownerName, propertyPath);

    /// <summary>
    /// Creates an immutable asset reference diagnostic snapshot.
    /// </summary>
    /// <param name="persistentId">
    /// The stable persistent identity used for lookup.
    /// </param>
    /// <param name="assetPath">
    /// The asset path consumed by create reference info; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="contentVersion">
    /// The content version consumed by create reference info; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="isLoaded">
    /// Whether is loaded behavior is enabled while create reference info executes.
    /// </param>
    /// <param name="lastSweepReachability">
    /// Whether last sweep reachability behavior is enabled while create reference info executes.
    /// </param>
    /// <param name="references">
    /// The references consumed by create reference info; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated asset reference info that represents the completed operation.
    /// </returns>
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
