using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_REMOVE_COMPONENT, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_COMPONENT_AREA, "Remove Component", order: 200)]
internal sealed class RemoveComponentCommand(SceneEdits edits) : EditorAction<ComponentEditorTarget>
{
    internal static EditorCommand command { get; } = new(InspectorInteractionIds.removeComponent);

    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => context.target.component is not Transform
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
    {
        if (context.target.component is not Transform)
            _ = edits.RemoveComponent(context.target.component);
    }
}
