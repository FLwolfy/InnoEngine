namespace Inno.Assets;

/// <summary>
/// Initialization options for <see cref="AssetManager"/>.
/// </summary>
public readonly struct AssetManagerOptions
{
    /// <summary>
    /// Root folder containing source assets.
    /// </summary>
    public string assetRoot { get; init; }
    /// <summary>
    /// Root folder containing imported runtime artifacts.
    /// </summary>
    public string artifactRoot { get; init; }
    /// <summary>
    /// Whether to enable file-system watch and hot-refresh.
    /// </summary>
    public bool enableFileSystemWatcher { get; init; }

    /// <summary>
    /// File watcher change coalescing delay in milliseconds.
    /// </summary>
    public int fileWatcherFlushDelayMs { get; init; }

    /// <summary>
    /// Creates options with sensible defaults for most projects.
    /// </summary>
    /// <param name="assetRoot">Source assets root folder.</param>
    /// <param name="artifactRoot">Artifacts root folder.</param>
    /// <returns>Initialized options value.</returns>
    public static AssetManagerOptions Create(string assetRoot, string artifactRoot)
    {
        return new AssetManagerOptions
        {
            assetRoot = assetRoot,
            artifactRoot = artifactRoot,
            enableFileSystemWatcher = true,
            fileWatcherFlushDelayMs = 80
        };
    }
}
