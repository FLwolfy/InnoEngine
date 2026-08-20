using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Engine.Scene;
using Inno.Editor.Panel.Hierarchy;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(HierarchyActions.SetActiveScene)]
[EditorMenu(HierarchyAreas.Hierarchy, "Set Active Scene", order: 100)]
internal sealed class SetActiveSceneCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? new EditorActionState(true, !ReferenceEquals(context.target, SceneManager.activeScene))
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
        => SceneManager.SetActiveScene(context.target);
}
