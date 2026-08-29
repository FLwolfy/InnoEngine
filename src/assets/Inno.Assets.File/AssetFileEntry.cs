using System.IO;
using Inno.Assets.Core;

namespace Inno.Assets.File;

/// <summary>
/// One node in the asset source filesystem index.
/// </summary>
public sealed class AssetFileEntry
{
    /// <summary>Gets the final source path segment, including its extension.</summary>
    public string name => Path.GetFileName(assetPath.localPath);

    /// <summary>Gets the final source path segment without its last extension.</summary>
    public string nameWithoutExtension => isDirectory ? name : Path.GetFileNameWithoutExtension(name);

    /// <summary>Gets the isolated source path.</summary>
    public AssetPath assetPath { get; internal set; } = AssetPath.Project(string.Empty);

    /// <summary>Gets the owning source mount identity.</summary>
    public AssetSourceId source => assetPath.source;

    /// <summary>Gets whether source mutations are forbidden.</summary>
    public bool isReadOnly { get; internal set; }

    /// <summary>Gets the isolated parent directory path.</summary>
    public AssetPath parentAssetPath { get; internal set; } = AssetPath.Project(string.Empty);
    /// <summary>Gets whether the entry represents a directory.</summary>
    public bool isDirectory { get; internal set; }
    /// <summary>Gets the normalized lower-case file extension.</summary>
    public string extension { get; internal set; } = string.Empty;
}
