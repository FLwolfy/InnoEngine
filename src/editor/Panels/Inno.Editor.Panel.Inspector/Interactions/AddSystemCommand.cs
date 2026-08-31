using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_ADD_SYSTEM, InspectorInteractionIds.C_SYSTEM_AREA)]
internal sealed class AddSystemCommand(SceneEdits edits) : EditorAction<GameScene, TypeRef>
{
    protected override EditorActionState Query(EditorActionContext<GameScene, TypeRef> context)
        => context.target.isLoaded && context.argument.isValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene, TypeRef> context)
        => _ = edits.AddSystem(context.target, context.argument.Resolve());
}
