using System;

using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorActions.AddComponent, InspectorAreas.Component)]
internal sealed class AddComponentCommand(SceneEdits edits) : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid && context.argument is Type
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        if (context.argument is Type componentType)
            _ = edits.AddComponent(context.target, componentType);
    }
}
