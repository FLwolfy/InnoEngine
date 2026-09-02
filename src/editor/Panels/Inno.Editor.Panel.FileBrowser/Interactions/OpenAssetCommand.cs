using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_OPEN, FileBrowserInteractionIds.C_AREA, priority: 100)]
internal sealed class OpenAssetCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return EditorActionState.hidden;
        return assetContext.isDirectory
            ? EditorActionState.hidden
            : EditorActionState.enabled;
    }

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return;
        if (assets.pipeline.TryLoad<AssetObject>(AssetPath.Parse(assetContext.relativePath), out AssetObject? asset) &&
            asset is not null &&
            context.interactions.For(FileBrowserInteractionIds.C_AREA, asset).Execute(
                FileBrowserInteractionIds.C_OPEN,
                assetContext.relativePath))
        {
            return;
        }
        _ = assets.Open(assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.assetPath.ToString(), out assetContext);
}
