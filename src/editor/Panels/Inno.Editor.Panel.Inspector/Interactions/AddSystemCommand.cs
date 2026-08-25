using System;

using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_ADD_SYSTEM, InspectorInteractionIds.C_SYSTEM_AREA)]
internal sealed class AddSystemCommand(SceneEdits edits) : EditorAction<GameScene, Type>
{
    internal static EditorCommand<Type> command { get; } = new(InspectorInteractionIds.addSystem);

    protected override EditorActionState Query(EditorActionContext<GameScene, Type> context)
        => context.target.isLoaded
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene, Type> context)
        => _ = edits.AddSystem(context.target, context.argument);
}
