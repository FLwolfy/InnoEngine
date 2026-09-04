using System.IO;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Batched file-system change event for asset source files.
/// </summary>
/// <param name="relativePath">
/// Changed path relative to watched root.
/// </param>
/// <param name="changeType">
/// File-system change type.
/// </param>
/// <param name="oldRelativePath">
/// Old path for rename operations.
/// </param>
public readonly struct AssetChangedEvent(string relativePath, WatcherChangeTypes changeType, string oldRelativePath = "")
{
    /// <summary>
    /// Changed path relative to watched root.
    /// </summary>
    public string relativePath { get; } = relativePath;

    /// <summary>
    /// Underlying file-system change type.
    /// </summary>
    public WatcherChangeTypes changeType { get; } = changeType;

    /// <summary>
    /// Old path relative to watched root for rename operations.
    /// Empty for non-rename changes.
    /// </summary>
    public string oldRelativePath { get; } = oldRelativePath ?? string.Empty;
}
