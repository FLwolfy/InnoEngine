namespace Inno.Assets;

/// <summary>
/// Describes one named output in an immutable artifact bundle.
/// </summary>
public sealed class AssetArtifactInfo
{
    /// <summary>
    /// Creates an artifact output descriptor.
    /// </summary>
    /// <param name="key">
    /// The immutable bundle identity that owns this output.
    /// </param>
    /// <param name="outputName">
    /// The stable output name used for artifact lookup.
    /// </param>
    /// <param name="absolutePath">
    /// The absolute path of the immutable artifact file.
    /// </param>
    /// <param name="contentHash">
    /// The normalized content fingerprint used for integrity verification.
    /// </param>
    /// <param name="length">
    /// The exact artifact length in bytes.
    /// </param>
    public AssetArtifactInfo(
        AssetArtifactKey key,
        string outputName,
        string absolutePath,
        string contentHash,
        long length)
    {
        this.key = key;
        this.outputName = outputName ?? string.Empty;
        this.absolutePath = absolutePath ?? string.Empty;
        this.contentHash = contentHash ?? string.Empty;
        this.length = length;
    }

    /// <summary>
    /// Gets the owning artifact bundle key.
    /// </summary>
    public AssetArtifactKey key { get; }

    /// <summary>
    /// Gets the stable output name.
    /// </summary>
    public string outputName { get; }

    /// <summary>
    /// Gets the absolute immutable output path.
    /// </summary>
    public string absolutePath { get; }

    /// <summary>
    /// Gets the output content fingerprint.
    /// </summary>
    public string contentHash { get; }

    /// <summary>
    /// Gets the output length in bytes.
    /// </summary>
    public long length { get; }
}
