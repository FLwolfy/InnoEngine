using System;

using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction("inspector/add-system", "panel/scene.inspector/system")]
internal sealed class AddSystemCommand(SceneEdits edits) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded && context.argument is Type
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (context.argument is Type systemType)
            _ = edits.AddSystem(context.target, systemType);
    }
}
