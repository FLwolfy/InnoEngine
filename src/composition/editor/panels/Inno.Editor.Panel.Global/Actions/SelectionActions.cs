using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction(GlobalInteractionIds.C_CLEAR_SELECTION)]
internal sealed class ClearEditorSelectionAction : EditorAction
{
    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => context.interactions.SetSelection(null);
}

[EditorAction(GlobalInteractionIds.C_SELECT)]
internal sealed class SelectEditorTargetAction : EditorAction<object>
{
    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<object> context)
        => context.interactions.SetSelection(context.target);
}
