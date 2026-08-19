using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Assets.Selection;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;

namespace Inno.Editor.Assets.Commands;

[EditorAction(EditorActionIds.Open, typeof(AssetSurface.Browser), priority: 100)]
internal sealed class OpenAssetCommand(AssetEditorModule assets) : EditorAction<AssetSelectionTarget>
{
    protected override EditorActionState Query(EditorActionContext<AssetSelectionTarget> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return EditorActionState.hidden;
        return assetContext.isDirectory
            ? EditorActionState.hidden
            : EditorActionState.enabled;
    }

    protected override void Execute(EditorActionContext<AssetSelectionTarget> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return;
        if (AssetManager.TryLoad<AssetObject>(assetContext.relativePath, out AssetObject? asset) &&
            asset is not null &&
            context.editor.Execute(EditorActionIds.Open, typeof(AssetSurface.Browser), asset, assetContext))
        {
            return;
        }
        _ = assets.Open(assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetSelectionTarget> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
