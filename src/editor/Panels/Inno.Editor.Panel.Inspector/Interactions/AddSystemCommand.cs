using Inno.Extensibility.Types;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_ADD_SYSTEM, InspectorInteractionIds.C_SYSTEM_AREA)]
internal sealed class AddSystemCommand(SceneEdits edits, TypeCatalog types)
    : EditorAction<GameScene, TypeRef>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<GameScene, TypeRef> context)
        => context.target.isLoaded && context.argument.IsValid(types)
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<GameScene, TypeRef> context)
        => _ = edits.AddSystem(context.target, context.argument.Resolve(types));
}
