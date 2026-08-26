using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_RESET_COMPONENT, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_COMPONENT_AREA, "Reset Component", order: 100)]
internal sealed class ResetComponentCommand(SceneEdits edits) : EditorAction<ComponentEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => !context.target.component.isDestroyed && context.target.component is not MissingGameComponent
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
        => edits.ResetComponent(context.target.component);
}
