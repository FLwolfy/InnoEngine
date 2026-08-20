using Inno.Assets.File;
using Inno.Editor.Panel.FileBrowser.AssetEditors;
using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Core.Input;
using Inno.Editor.Panel.FileBrowser;

namespace Inno.Editor.Panel.FileBrowser.Commands;

[EditorAction(FileBrowserActions.Delete, priority: 100)]
[EditorMenu(FileBrowserAreas.Browser, "Delete", order: 200)]
[EditorShortcut(FileBrowserAreas.Browser, KeyCode.Delete)]
internal sealed class DeleteAssetCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => TryGetAssetContext(context, out _) ? EditorActionState.enabled : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (TryGetAssetContext(context, out AssetEditorContext? assetContext) && assetContext is not null)
            _ = assets.Delete(assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
