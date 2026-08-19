using System;

using Inno.Assets;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;

namespace Inno.Editor.Assets.Commands;

[EditorAction(AssetActionIds.CreateFolder, typeof(AssetSurface.Browser))]
internal sealed class CreateAssetFolderCommand(AssetEditorModule assets) : EditorAction
{
    public override EditorActionState Query(EditorActionContext context)
        => AssetManager.isInitialized ? EditorActionState.enabled : EditorActionState.disabled;

    public override void Execute(EditorActionContext context)
    {
        string parent = Normalize(assets.browser.currentDirectory);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(candidate, out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        AssetManager.CreateDirectory(candidate);
        assets.browser.Select(context.editor, candidate);
        _ = assets.TryCreateContext(
            context.editor,
            candidate,
            out AssetEditorContext? assetContext);
        if (assetContext is not null)
            assets.BeginRename(assetContext);
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}
