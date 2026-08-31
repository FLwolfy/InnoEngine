using System;

namespace Inno.Assets;

/// <summary>Controls cleanup of rebuildable asset database data.</summary>
public readonly struct AssetCacheOptions
{
    /// <summary>Gets the maximum artifact cache size in bytes, or zero for no size limit.</summary>
    public long maximumSizeBytes { get; init; }

    /// <summary>Gets the minimum age of an unreachable artifact before it can be collected.</summary>
    public TimeSpan garbageCollectionGracePeriod { get; init; }

    /// <summary>Creates the default cache policy.</summary>
    /// <returns>The default cache options.</returns>
    public static AssetCacheOptions CreateDefault()
    {
        return new AssetCacheOptions
        {
            maximumSizeBytes = 4L * 1024 * 1024 * 1024,
            garbageCollectionGracePeriod = TimeSpan.FromDays(7)
        };
    }
}
