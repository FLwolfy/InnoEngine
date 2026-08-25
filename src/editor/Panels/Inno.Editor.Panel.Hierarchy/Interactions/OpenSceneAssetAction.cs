using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction("editor/open", priority: 200)]
internal sealed class OpenSceneAssetAction(EditorSceneWorkspace workspace) : EditorAction<SceneAsset>
{
    protected override void Execute(EditorActionContext<SceneAsset> context)
    {
        if (context.argument is not string relativePath)
            return;
        GameScene scene = workspace.OpenScene(relativePath);
        _ = context.interactions.For("panel/scene.hierarchy", scene).Select();
    }
}
