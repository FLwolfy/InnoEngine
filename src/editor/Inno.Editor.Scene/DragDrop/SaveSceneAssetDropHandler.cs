using Inno.Editor.Assets;
using Inno.Editor.Assets.DragDrop;
using Inno.Editor.Assets.Selection;

using Inno.Editor.Scene;
using Inno.Editor.Scene.Workspace;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

[EditorDrop(typeof(AssetSurface.Browser))]
internal sealed class SaveSceneAssetDropHandler
    : EditorDrop<GameScene, AssetDirectoryDropTarget>
{
    private readonly EditorSceneWorkspace m_workspace;
    internal SaveSceneAssetDropHandler(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    protected override EditorDropStatus Query(
        EditorDropContext<GameScene, AssetDirectoryDropTarget> context)
        => context.source.isLoaded
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;

    protected override EditorDropResult Drop(
        EditorDropContext<GameScene, AssetDirectoryDropTarget> context)
    {
        string path = m_workspace.SaveSceneToDirectory(context.source, context.target.relativePath);
        var selection = new AssetSelectionTarget(path);
        _ = context.editor.Select(typeof(AssetSurface.Browser), selection);
        return EditorDropResult.Accepted(selection);
    }
}
