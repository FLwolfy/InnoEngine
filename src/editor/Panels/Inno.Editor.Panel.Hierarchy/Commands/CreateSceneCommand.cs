using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Panel.Hierarchy.Workspace;
using Inno.Engine.Scene;
using Inno.Editor.Panel.Hierarchy;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(HierarchyActions.CreateScene)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Scene", order: 300, separatorBefore: true)]
internal sealed class CreateSceneCommand(EditorSceneWorkspace workspace) : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
        GameScene scene = workspace.CreateScene();
        _ = context.interactions.For(context.area, scene).Select();
    }
}
