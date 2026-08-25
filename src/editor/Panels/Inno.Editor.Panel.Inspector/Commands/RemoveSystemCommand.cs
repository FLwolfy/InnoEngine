using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction("inspector/remove-system", priority: 100)]
[EditorMenu("panel/scene.inspector/system", "Remove System", order: 200)]
internal sealed class RemoveSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => _ = edits.RemoveSystem(context.target.scene, context.target.system);
}
