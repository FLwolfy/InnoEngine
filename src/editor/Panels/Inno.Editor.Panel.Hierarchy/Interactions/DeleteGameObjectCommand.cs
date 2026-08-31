using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_DELETE_GAME_OBJECT, priority: 100)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Delete", order: 300)]
[EditorShortcut(HierarchyInteractionIds.C_AREA, KeyCode.Delete)]
internal sealed class DeleteGameObjectCommand(SceneEdits edits) : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        if (!context.target.isRuntimeValid)
            return;
        _ = edits.DeleteGameObject(context.target);
        if (context.interactions.selection.TryGet(out GameObject? selected) && ReferenceEquals(selected, context.target))
            _ = context.interactions.For(context.area).Select();
    }
}
