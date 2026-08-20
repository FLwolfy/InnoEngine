using Inno.Editor.Interactions.Actions;
using Inno.Editor.Panel.Hierarchy.Workspace;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Editor.Panel.Hierarchy;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(EditorActions.Open, priority: 200)]
internal sealed class OpenSceneAssetAction(EditorSceneWorkspace workspace) : EditorAction<SceneAsset>
{
    protected override void Execute(EditorActionContext<SceneAsset> context)
    {
        if (context.argument is not string relativePath)
            return;
        GameScene scene = workspace.OpenScene(relativePath);
        _ = context.interactions.For(HierarchyAreas.Hierarchy, scene).Select();
    }
}
