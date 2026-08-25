using System;

using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_ADD_COMPONENT, InspectorInteractionIds.C_COMPONENT_AREA)]
internal sealed class AddComponentCommand(SceneEdits edits) : EditorAction<GameObject, Type>
{
    internal static EditorCommand<Type> command { get; } = new(InspectorInteractionIds.addComponent);

    protected override EditorActionState Query(EditorActionContext<GameObject, Type> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject, Type> context)
        => _ = edits.AddComponent(context.target, context.argument);
}
