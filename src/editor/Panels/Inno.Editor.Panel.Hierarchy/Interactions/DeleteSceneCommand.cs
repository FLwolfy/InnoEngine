using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_DELETE_SCENE, priority: 100)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Delete", order: 400, separatorBefore: true)]
[EditorShortcut(HierarchyInteractionIds.C_AREA, KeyCode.Delete)]
internal sealed class DeleteSceneCommand(SceneEdits edits) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target is { isLoaded: true, isDestroyed: false }
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (!edits.CloseScene(context.target))
            return;
        if (context.interactions.selection.TryGet(out GameScene? selected) && ReferenceEquals(selected, context.target))
            _ = context.interactions.For(context.area).Select();
    }
}
