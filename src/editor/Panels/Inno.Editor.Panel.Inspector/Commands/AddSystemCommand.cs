using System;

using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

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
        {
            SceneSnapshotOperation.Execute(
                context,
                $"Add {systemType.Name}",
                context.target,
                () => _ = context.target.AddSystem(systemType));
        }
    }
}
