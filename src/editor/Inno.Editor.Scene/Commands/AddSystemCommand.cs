using System;

using Inno.Editor.Core.Commands;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.AddSystem, typeof(SceneSurface.AddSystem))]
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
