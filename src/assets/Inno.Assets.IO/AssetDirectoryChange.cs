namespace Inno.Assets.IO;

public readonly struct AssetDirectoryChange(
    AssetDirectoryChangeKind kind,
    string fullPath,
    string relativePath,
    string? oldFullPath = null,
    string? oldRelativePath = null)
{
    public readonly AssetDirectoryChangeKind kind = kind;
    public readonly string fullPath = fullPath;
    public readonly string relativePath = relativePath;
    public readonly string? oldFullPath = oldFullPath;
    public readonly string? oldRelativePath = oldRelativePath;
}