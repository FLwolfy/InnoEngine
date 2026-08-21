using System;

using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

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
        {
            SceneSnapshotOperation.Execute(
                context,
                $"Add {componentType.Name}",
                context.target.scene,
                () => _ = context.target.AddComponent(componentType));
        }
    }
}
