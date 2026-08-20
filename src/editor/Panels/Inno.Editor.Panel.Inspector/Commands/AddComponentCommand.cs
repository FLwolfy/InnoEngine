using System;

using Inno.Editor.Interactions.Actions;
using Inno.Engine.Scene;
using Inno.Editor.Panel.Inspector;

namespace Inno.Editor.Panel.Inspector.Commands;

[EditorAction(InspectorActions.AddComponent, InspectorAreas.Component)]
internal sealed class AddComponentCommand : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid && context.argument is Type
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        if (context.argument is Type componentType)
            _ = context.target.AddComponent(componentType);
    }
}
