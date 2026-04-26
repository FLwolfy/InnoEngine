namespace Inno.Editor.Core;

/// <summary>
/// Stores current editor selection and asset navigation state.
/// </summary>
public sealed class EditorSelectionState
{
    /// <summary>
    /// Gets the currently focused directory path (relative to Asset root).
    /// Empty means asset root.
    /// </summary>
    public string currentDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the currently selected asset entry path (relative to Asset root).
    /// </summary>
    public string? selectedPath { get; private set; }

    /// <summary>
    /// Sets the current directory.
    /// </summary>
    /// <param name="relativePath">Directory path relative to asset root.</param>
    public void SetCurrentDirectory(string relativePath)
    {
        currentDirectory = NormalizeRelativePath(relativePath);
    }

    /// <summary>
    /// Sets the selected asset path.
    /// </summary>
    /// <param name="relativePath">Entry path relative to asset root.</param>
    public void SetSelectedPath(string? relativePath)
    {
        selectedPath = string.IsNullOrWhiteSpace(relativePath) ? null : NormalizeRelativePath(relativePath!);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", System.StringComparison.Ordinal))
            path = path[2..];
        while (path.StartsWith("/", System.StringComparison.Ordinal))
            path = path[1..];
        while (path.EndsWith("/", System.StringComparison.Ordinal))
            path = path[..^1];

        return path == "." ? string.Empty : path;
    }
}
