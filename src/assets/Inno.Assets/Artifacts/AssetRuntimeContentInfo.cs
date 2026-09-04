using System.Collections.Generic;

namespace Inno.Assets;

/// <summary>
/// Describes one deployed runtime-only asset content snapshot.
/// </summary>
public sealed class AssetRuntimeContentInfo
{
    /// <summary>
    /// Creates an immutable runtime content summary.
    /// </summary>
    /// <param name="sources">
    /// Source identities represented by the deployed catalog.
    /// </param>
    /// <param name="assetCount">
    /// Number of runtime-scoped assets in the deployed catalog.
    /// </param>
    /// <param name="artifactBundleCount">
    /// Number of unique content-addressed bundles.
    /// </param>
    /// <param name="totalBytes">
    /// Total bytes copied into the runtime content root.
    /// </param>
    public AssetRuntimeContentInfo(
        IReadOnlyList<AssetSourceId> sources,
        int assetCount,
        int artifactBundleCount,
        long totalBytes)
    {
        this.sources = sources;
        this.assetCount = assetCount;
        this.artifactBundleCount = artifactBundleCount;
        this.totalBytes = totalBytes;
    }

    /// <summary>
    /// Gets the source identities represented by the deployed catalog.
    /// </summary>
    public IReadOnlyList<AssetSourceId> sources { get; }

    /// <summary>
    /// Gets the number of runtime-scoped assets in the deployed catalog.
    /// </summary>
    public int assetCount { get; }

    /// <summary>
    /// Gets the number of unique content-addressed bundles.
    /// </summary>
    public int artifactBundleCount { get; }

    /// <summary>
    /// Gets the total bytes copied into the runtime content root.
    /// </summary>
    public long totalBytes { get; }
}
