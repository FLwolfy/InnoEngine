using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyActions.CreateScene)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Scene", order: 300, separatorBefore: true)]
internal sealed class CreateSceneCommand(SceneEdits edits) : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
        GameScene scene = edits.CreateScene();
        _ = context.interactions.For(context.area, scene).Select();
    }
}
