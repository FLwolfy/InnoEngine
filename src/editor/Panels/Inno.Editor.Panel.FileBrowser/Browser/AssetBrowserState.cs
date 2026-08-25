
using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Stores asset browser navigation independently from global object selection.</summary>
public sealed class AssetBrowserState
{
    private readonly EditorInteractions m_interactions;

    /// <summary>Creates Asset Browser navigation state.</summary>
    /// <param name="interactions">The active editor interaction entry point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="interactions"/> is <see langword="null"/>.</exception>
    public AssetBrowserState(EditorInteractions interactions)
    {
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>Gets the current source-relative directory.</summary>
    public string currentDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the selected asset path when the editor-wide target belongs to the Asset Browser.
    /// </summary>
    /// <param name="context">The shared editor context containing the global selection.</param>
    /// <returns>The normalized source-relative path, or <see langword="null"/> when another target type is selected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public string? GetSelectedPath(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (m_interactions.selection.selectedTarget as Inno.Assets.File.AssetFileEntry)?.relativePath;
    }

    /// <summary>
    /// Sets the current Asset Browser directory after normalizing path separators and root notation.
    /// </summary>
    /// <param name="relativePath">The source-relative directory path, or an empty value for the Asset root.</param>
    public void SetCurrentDirectory(string relativePath)
        => currentDirectory = Normalize(relativePath);

    /// <summary>
    /// Selects an asset path through the editor-wide selection state, or clears Asset selection.
    /// </summary>
    /// <param name="context">The shared editor context whose selection should be updated.</param>
    /// <param name="relativePath">The source-relative path to select, or an empty value to clear selection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public void Select(EditorContext context, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        object? target = null;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            if (Inno.Assets.AssetManager.TryGetFileSystemEntry(
                    Normalize(relativePath),
                    out Inno.Assets.File.AssetFileEntry entry))
            {
                target = entry;
            }
        }
        _ = m_interactions.For("panel/asset.file-browser", target).Select();
    }

    private static string Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;
        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return path.Trim('/');
    }
}
