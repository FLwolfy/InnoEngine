using Inno.Editor.Interactions;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyActions.DeleteScene, priority: 100)]
[EditorMenu(HierarchyAreas.Hierarchy, "Delete", order: 400, separatorBefore: true)]
[EditorShortcut(HierarchyAreas.Hierarchy, KeyCode.Delete)]
internal sealed class DeleteSceneCommand(EditorSceneWorkspace workspace) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target is { isLoaded: true, isDestroyed: false } && workspace.scenes.Count > 1
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (!workspace.CloseScene(context.target))
            return;
        if (context.interactions.selection.TryGet(out GameScene? selected) && ReferenceEquals(selected, context.target))
            _ = context.interactions.For(context.area).Select();
    }
}
