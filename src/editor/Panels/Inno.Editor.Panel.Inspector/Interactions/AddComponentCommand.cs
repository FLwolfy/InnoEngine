using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_ADD_COMPONENT, InspectorInteractionIds.C_COMPONENT_AREA)]
internal sealed class AddComponentCommand(SceneEdits edits) : EditorAction<GameObject, TypeRef>
{
    protected override EditorActionState Query(EditorActionContext<GameObject, TypeRef> context)
        => context.target.isRuntimeValid && context.argument.isValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject, TypeRef> context)
        => _ = edits.AddComponent(context.target, context.argument.Resolve());
}
