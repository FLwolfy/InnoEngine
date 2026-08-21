using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyActions.CreateScene)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Scene", order: 300, separatorBefore: true)]
internal sealed class CreateSceneCommand(EditorSceneWorkspace workspace) : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
        GameScene? scene = null;
        SceneWorkspaceOperation.Execute(
            context,
            "Create Scene",
            workspace,
            () => scene = workspace.CreateScene());
        if (scene is null)
            return;
        _ = context.interactions.For(context.area, scene).Select();
    }
}
