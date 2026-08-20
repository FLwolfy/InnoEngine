using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Panel.Inspector;

namespace Inno.Editor.Panel.Inspector.Commands;

[EditorAction(InspectorActions.ResetSystem, priority: 100)]
[EditorMenu(InspectorAreas.System, "Reset System", order: 100)]
internal sealed class ResetSystemCommand : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => context.target.scene.ResetSystem(context.target.system);
}
