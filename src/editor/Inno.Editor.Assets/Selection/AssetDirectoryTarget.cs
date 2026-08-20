namespace Inno.Editor.Assets.Selection;

/// <summary>
/// Identifies an Asset Browser directory that receives background operations such as creating a new entry.
/// </summary>
/// <param name="relativePath">The directory path relative to the Asset root, or an empty string for the root directory.</param>
public sealed record AssetDirectoryTarget(string relativePath);
