using System;
using System.Collections.Generic;

using Inno.Assets.Pipeline;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Initialization options for <see cref="AssetPipeline"/>.
/// </summary>
public readonly struct AssetPipelineOptions
{
    /// <summary>
    /// Gets whether the pipeline reconciles authoring sources or consumes a deployed artifact catalog.
    /// </summary>
    public AssetPipelineMode mode { get; init; }

    /// <summary>
    /// Gets the root folder containing source assets.
    /// </summary>
    public string assetRoot { get; init; }

    /// <summary>
    /// Gets the root folder containing rebuildable project data.
    /// </summary>
    public string libraryRoot { get; init; }

    /// <summary>
    /// Gets whether file-system watching is enabled.
    /// </summary>
    public bool enableFileSystemWatcher { get; init; }

    /// <summary>
    /// Gets the watcher change coalescing delay in milliseconds.
    /// </summary>
    public int fileWatcherFlushDelayMs { get; init; }

    /// <summary>
    /// Gets the source filtering policy.
    /// </summary>
    public AssetSourcePolicy? sourcePolicy { get; init; }

    /// <summary>
    /// Gets the complete source mount snapshot, or null to mount only <see cref="assetRoot"/>.
    /// </summary>
    public IReadOnlyList<AssetSourceMount>? sourceMounts { get; init; }

    /// <summary>
    /// Gets the rebuildable cache policy.
    /// </summary>
    public AssetCacheOptions cacheOptions { get; init; }

    /// <summary>
    /// Creates options with sensible defaults for most projects.
    /// </summary>
    /// <param name="assetRoot">
    /// Source assets root folder.
    /// </param>
    /// <param name="libraryRoot">
    /// Rebuildable Library folder.
    /// </param>
    /// <returns>
    /// Initialized options value.
    /// </returns>
    public static AssetPipelineOptions Create(string assetRoot, string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(assetRoot));
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Library root is required.", nameof(libraryRoot));

        return new AssetPipelineOptions
        {
            mode = AssetPipelineMode.Authoring,
            assetRoot = assetRoot,
            libraryRoot = libraryRoot,
            enableFileSystemWatcher = true,
            fileWatcherFlushDelayMs = 80,
            sourcePolicy = AssetSourcePolicy.defaultPolicy,
            sourceMounts = [new AssetSourceMount(Inno.Assets.AssetSourceId.project, assetRoot, isReadOnly: false)],
            cacheOptions = AssetCacheOptions.CreateDefault()
        };
    }
}
