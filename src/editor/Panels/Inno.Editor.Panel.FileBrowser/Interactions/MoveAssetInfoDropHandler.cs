using Inno.Assets.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Moves imported asset drag payloads into File Browser directory targets.
/// </summary>
[EditorDrop(FileBrowserInteractionIds.C_AREA, priority: 200)]
internal sealed class MoveAssetInfoDropHandler(AssetEditorModule assets)
    : EditorDrop<AssetInfo, string>
{
    /// <inheritdoc />
    protected override EditorDropStatus Query(
        EditorDropContext<AssetInfo, string> context)
        => assets.CanMoveToDirectory(context.source.assetPath.ToString(), context.target)
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    /// <inheritdoc />
    protected override EditorDropResult Drop(
        EditorDropContext<AssetInfo, string> context)
    {
        if (!Query(context).canDrop)
            return EditorDropResult.rejected;
        Inno.Assets.File.AssetFileEntry moved = assets.MoveToDirectoryWithHistory(
            context.source.assetPath.ToString(),
            context.target,
            context.interactions.history);
        return EditorDropResult.Accepted(moved, moved);
    }
}
