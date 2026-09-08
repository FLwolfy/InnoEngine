namespace Inno.Assets;

/// <summary>
/// Identifies the durable source role recorded by an asset metadata sidecar.
/// </summary>
public enum AssetSourceKind
{
    /// <summary>
    /// A regular imported source file.
    /// </summary>
    File,

    /// <summary>
    /// A source directory with persistent identity but no runtime artifact.
    /// </summary>
    Directory
}
