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
    private readonly AssetEditorModule m_assets;

    internal SaveSceneAssetDropHandler(EditorSceneWorkspace workspace, AssetEditorModule assets)
    {
        m_workspace = workspace;
        m_assets = assets;
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
        m_assets.browser.Select(context.editor, path);
        return EditorDropResult.Accepted(new AssetSelectionTarget(path));
    }
}
