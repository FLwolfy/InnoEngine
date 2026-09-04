using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction(GlobalInteractionIds.C_UNDO, priority: 1000)]
[EditorMenu(GlobalInteractionIds.C_MAIN_MENU_AREA, "Edit/Undo", order: 100)]
[EditorShortcut(KeyCode.Z, primary: true)]
internal sealed class UndoEditorAction : EditorAction
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
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canUndo,
            displayName: context.history.undoName is { } name
                ? context.history.undoUnavailableReason is { } reason
                    ? $"Undo {name} ({reason})"
                    : $"Undo {name}"
                : "Undo");

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => _ = context.history.Undo();
}

[EditorAction(GlobalInteractionIds.C_REDO, priority: 1000)]
[EditorMenu(GlobalInteractionIds.C_MAIN_MENU_AREA, "Edit/Redo", order: 110)]
[EditorShortcut(KeyCode.Z, KeyModifier.Shift, primary: true)]
[EditorShortcut(KeyCode.Y, primary: true)]
internal sealed class RedoEditorAction : EditorAction
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
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canRedo,
            displayName: context.history.redoName is { } name
                ? context.history.redoUnavailableReason is { } reason
                    ? $"Redo {name} ({reason})"
                    : $"Redo {name}"
                : "Redo");

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => _ = context.history.Redo();
}
