using Inno.Editor.Assets.Selection;

using System;

using Inno.Editor.Core;

namespace Inno.Editor.Assets.Selection;

/// <summary>Stores asset browser navigation independently from global object selection.</summary>
public sealed class AssetBrowserState
{
    /// <summary>Gets the current source-relative directory.</summary>
    public string currentDirectory { get; private set; } = string.Empty;

    /// <summary>Gets the selected asset path, or <see langword="null"/> for another target type.</summary>
    public string? GetSelectedPath(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (context.selection.selectedTarget as AssetSelectionTarget)?.relativePath;
    }

    /// <summary>Sets the current source-relative directory.</summary>
    public void SetCurrentDirectory(string relativePath)
        => currentDirectory = Normalize(relativePath);

    /// <summary>Selects an asset path through the editor-wide selection service.</summary>
    public void Select(EditorContext context, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            context.selection.Clear();
            return;
        }

        context.selection.Select(new AssetSelectionTarget(Normalize(relativePath)));
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
