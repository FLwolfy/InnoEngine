using System;

using Inno.Editor.Core.Commands;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.AddComponent, typeof(SceneSurface.AddComponent))]
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
