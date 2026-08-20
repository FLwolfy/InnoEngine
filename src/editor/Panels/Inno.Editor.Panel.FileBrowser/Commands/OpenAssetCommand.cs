using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(EditorActions.Open, FileBrowserAreas.Browser, priority: 100)]
internal sealed class OpenAssetCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return EditorActionState.hidden;
        return assetContext.isDirectory
            ? EditorActionState.hidden
            : EditorActionState.enabled;
    }

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return;
        if (AssetManager.TryLoad<AssetObject>(assetContext.relativePath, out AssetObject? asset) &&
            asset is not null &&
            context.interactions.For(FileBrowserAreas.Browser, asset).Execute(
                EditorActions.Open,
                assetContext.relativePath))
        {
            return;
        }
        _ = assets.Open(assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
