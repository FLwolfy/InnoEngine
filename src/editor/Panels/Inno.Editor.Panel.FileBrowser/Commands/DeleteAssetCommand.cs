using Inno.Assets.File;
using Inno.Editor.Interactions;
using Inno.Core.Input;

namespace Inno.Editor.Panel.FileBrowser;

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
            _ = AssetDeleteOperation.Execute(context, assets, assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
