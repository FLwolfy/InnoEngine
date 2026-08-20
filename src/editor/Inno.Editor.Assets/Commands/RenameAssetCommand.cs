using Inno.Editor.Assets.Selection;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Core.Input;

namespace Inno.Editor.Assets.Commands;

[EditorAction(EditorActionIds.Rename, priority: 100)]
[EditorMenu(typeof(AssetSurface.ContextMenu), "Rename", order: 100)]
[EditorShortcut(typeof(FileBrowser.FileBrowserPanel), KeyCode.F2)]
internal sealed class RenameAssetCommand(AssetEditorModule assets) : EditorAction<AssetSelectionTarget>
{
    protected override EditorActionState Query(EditorActionContext<AssetSelectionTarget> context)
        => TryGetAssetContext(context, out _) ? EditorActionState.enabled : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetSelectionTarget> context)
    {
        if (TryGetAssetContext(context, out AssetEditorContext? assetContext) && assetContext is not null)
        {
            _ = BeginInteraction(
                context,
                assetContext.name,
                value => assets.Rename(assetContext, value),
                value => assets.ValidateRename(assetContext, value));
        }
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetSelectionTarget> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
