using System;

using Inno.Editor.Interactions.Actions;
using Inno.Engine.Scene;
using Inno.Editor.Panel.Inspector;

namespace Inno.Editor.Panel.Inspector.Commands;

[EditorAction(InspectorActions.AddSystem, InspectorAreas.System)]
internal sealed class AddSystemCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded && context.argument is Type
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (context.argument is Type systemType)
            _ = context.target.AddSystem(systemType);
    }
}
