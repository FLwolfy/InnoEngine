using System.IO;

namespace Inno.Assets.File;

/// <summary>
/// One node in the asset source filesystem index.
/// </summary>
public sealed class AssetFileEntry
{
    /// <summary>Gets the final source path segment, including its extension.</summary>
    public string name => Path.GetFileName(relativePath);

    /// <summary>Gets the final source path segment without its last extension.</summary>
    public string nameWithoutExtension => isDirectory ? name : Path.GetFileNameWithoutExtension(name);

    /// <summary>Gets the source-relative entry path.</summary>
    public string relativePath { get; internal set; } = string.Empty;
    /// <summary>Gets the source-relative parent path.</summary>
    public string parentRelativePath { get; internal set; } = string.Empty;
    /// <summary>Gets whether the entry represents a directory.</summary>
    public bool isDirectory { get; internal set; }
    /// <summary>Gets the normalized lower-case file extension.</summary>
    public string extension { get; internal set; } = string.Empty;
}
