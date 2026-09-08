using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction(GlobalInteractionIds.C_TOGGLE_PANEL, GlobalInteractionIds.C_MAIN_MENU_AREA)]
internal sealed class TogglePanelAction : EditorArgumentAction<string>
{
    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionArgumentContext<string> context)
        => _ = context.interactions.TogglePanel(context.argument);
}
