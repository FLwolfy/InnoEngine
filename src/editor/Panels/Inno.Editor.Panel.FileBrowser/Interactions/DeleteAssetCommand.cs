using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;
using Inno.Core.Input;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_DELETE, priority: 100)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Delete", order: 200)]
[EditorShortcut(FileBrowserInteractionIds.C_AREA, KeyCode.Delete)]
internal sealed class DeleteAssetCommand(AssetEditorModule assets) : EditorAction<AssetFileEntry>
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
        => !context.target.isReadOnly && TryGetAssetContext(context, out _)
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (TryGetAssetContext(context, out AssetEditorContext? assetContext) && assetContext is not null)
            _ = assets.DeleteWithHistory(context, assetContext);
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.assetPath.ToString(), out assetContext);
}
