using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Centralizes the internal runtime contract consumed by the asset loading assembly.
/// </summary>
internal static class AssetRuntimeAccess
{
    internal static string GetSourceHash(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.sourceHash;
    }

    internal static void Initialize(
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

    internal static void UpdateSourcePath(AssetObject asset, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.UpdateSourcePath(relativePath);
    }

    internal static void Release(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.ReleaseRuntimeResources();
    }

    internal static AssetReferenceLocation CreateReferenceLocation(
        AssetReferenceKind kind,
        Guid ownerId,
        string ownerName,
        string propertyPath)
        => new(kind, ownerId, ownerName, propertyPath);

    internal static AssetReferenceInfo CreateReferenceInfo(
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
