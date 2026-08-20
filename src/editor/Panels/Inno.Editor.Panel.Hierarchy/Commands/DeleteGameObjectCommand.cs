using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Panel.Hierarchy;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(HierarchyActions.DeleteGameObject, priority: 100)]
[EditorMenu(HierarchyAreas.Hierarchy, "Delete", order: 300)]
[EditorShortcut(HierarchyAreas.Hierarchy, KeyCode.Delete)]
internal sealed class DeleteGameObjectCommand : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        if (!context.target.isRuntimeValid)
            return;
        _ = context.target.scene.DestroyObject(context.target);
        if (context.interactions.selection.TryGet(out GameObject? selected) && ReferenceEquals(selected, context.target))
            _ = context.interactions.For(context.area).Select();
    }
}
