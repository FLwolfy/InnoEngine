using Inno.Assets;
using Inno.Assets.File;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop("panel/asset.file-browser")]
internal sealed class SaveSceneAssetDropHandler
    : EditorDrop<GameScene, string>
{
    private readonly EditorSceneWorkspace m_workspace;
    internal SaveSceneAssetDropHandler(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    protected override EditorDropStatus Query(
        EditorDropContext<GameScene, string> context)
        => context.source.isLoaded
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    protected override EditorDropResult Drop(
        EditorDropContext<GameScene, string> context)
    {
        string path = m_workspace.SaveSceneToDirectory(context.source, context.target);
        if (!AssetManager.TryGetFileSystemEntry(path, out AssetFileEntry selection))
            return EditorDropResult.rejected;
        _ = context.interactions.For(context.area, selection).Select();
        return EditorDropResult.Accepted(selection);
    }
}
