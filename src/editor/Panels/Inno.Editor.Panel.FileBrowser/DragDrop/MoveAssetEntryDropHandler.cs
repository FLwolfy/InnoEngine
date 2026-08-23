using Inno.Assets.File;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Moves directory drag payloads into File Browser directory targets.
/// </summary>
[EditorDrop(FileBrowserAreas.Browser, priority: 200)]
internal sealed class MoveAssetEntryDropHandler(AssetEditorModule assets)
    : EditorDrop<AssetFileEntry, string>
{
    /// <inheritdoc />
    protected override EditorDropStatus Query(
        EditorDropContext<AssetFileEntry, string> context)
        => assets.CanMoveToDirectory(context.source.relativePath, context.target)
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    /// <inheritdoc />
    protected override EditorDropResult Drop(
        EditorDropContext<AssetFileEntry, string> context)
    {
        if (!Query(context).canDrop)
            return EditorDropResult.rejected;
        AssetFileEntry moved = assets.MoveToDirectoryWithHistory(
            context.source.relativePath,
            context.target,
            context.interactions.history);
        return EditorDropResult.Accepted(moved, moved);
    }
}
