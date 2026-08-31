using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction(GlobalInteractionIds.C_TOGGLE_PANEL, GlobalInteractionIds.C_MAIN_MENU_AREA)]
internal sealed class TogglePanelAction : EditorArgumentAction<string>
{
    protected override void Execute(EditorActionArgumentContext<string> context)
        => _ = context.interactions.TogglePanel(context.argument);
}
