using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_OPEN, priority: 200)]
internal sealed class OpenSceneAssetAction(IEditorSceneWorkspace workspace) : EditorAction<SceneAsset, string>
{
    protected override void Execute(EditorActionContext<SceneAsset, string> context)
    {
        GameScene scene = workspace.Open(context.argument);
        _ = context.interactions.For(HierarchyInteractionIds.C_AREA, scene).Select();
    }
}
