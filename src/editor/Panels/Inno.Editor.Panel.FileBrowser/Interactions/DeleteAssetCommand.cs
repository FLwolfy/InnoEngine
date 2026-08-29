using Inno.Assets.File;
using Inno.Editor.Interactions;
using Inno.Core.Input;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_DELETE, priority: 100)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Delete", order: 200)]
[EditorShortcut(FileBrowserInteractionIds.C_AREA, KeyCode.Delete)]
internal sealed class DeleteAssetCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => !context.target.isReadOnly && TryGetAssetContext(context, out _)
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (TryGetAssetContext(context, out AssetEditorContext? assetContext) && assetContext is not null)
            _ = assets.DeleteWithHistory(context, assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
