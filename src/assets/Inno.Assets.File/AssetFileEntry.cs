namespace Inno.Assets.File;

/// <summary>
/// One node in the asset source filesystem index.
/// </summary>
public sealed class AssetFileEntry
{
    public string relativePath { get; internal set; } = string.Empty;
    public string parentRelativePath { get; internal set; } = string.Empty;
    public bool isDirectory { get; internal set; }
    public string extension { get; internal set; } = string.Empty;
}
