namespace Inno.Editor.Core;

/// <summary>
/// Identifies a selected asset file-system entry by stable relative path.
/// </summary>
/// <param name="relativePath">Entry path relative to the asset root.</param>
public sealed record AssetSelectionTarget(string relativePath);
