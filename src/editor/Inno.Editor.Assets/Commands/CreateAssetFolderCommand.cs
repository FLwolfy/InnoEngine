using System;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Assets.Selection;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;

namespace Inno.Editor.Assets.Commands;

[EditorAction(AssetActionIds.CreateFolder, typeof(AssetSurface.ContextMenu))]
[EditorMenu(typeof(AssetSurface.ContextMenu), "Create/Folder", order: 100)]
internal sealed class CreateAssetFolderCommand : EditorAction<AssetDirectoryTarget>
{
    protected override EditorActionState Query(EditorActionContext<AssetDirectoryTarget> context)
        => AssetManager.isInitialized && IsDirectory(context.target.relativePath)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<AssetDirectoryTarget> context)
    {
        string parent = Normalize(context.target.relativePath);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(candidate, out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        AssetManager.CreateDirectory(candidate);
        var target = new AssetSelectionTarget(candidate);
        _ = context.editor.Select(typeof(AssetSurface.Browser), target);
        _ = context.editor.Execute(
            EditorActionIds.Rename,
            typeof(AssetSurface.Browser),
            target);
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static bool IsDirectory(string relativePath)
    {
        string normalized = Normalize(relativePath);
        return string.IsNullOrEmpty(normalized) ||
               AssetManager.TryGetFileSystemEntry(normalized, out AssetFileEntry entry) &&
               entry.isDirectory;
    }
}
