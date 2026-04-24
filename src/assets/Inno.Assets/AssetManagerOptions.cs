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
    /// Whether to register built-in importers during initialization.
    /// </summary>
    public bool autoRegisterBuiltInImporters { get; init; }
    /// <summary>
    /// Whether to discover and register importers through <c>TypeCache</c>.
    /// </summary>
    public bool autoRegisterImportersFromTypeCache { get; init; }

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
            autoRegisterBuiltInImporters = true,
            autoRegisterImportersFromTypeCache = false
        };
    }
}
