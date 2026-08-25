using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction(GlobalInteractionIds.C_TOGGLE_PANEL, GlobalInteractionIds.C_MAIN_MENU_AREA)]
internal sealed class TogglePanelAction : EditorArgumentAction<EditorPanelId>
{
    internal static EditorCommand<EditorPanelId> command { get; } =
        new(GlobalInteractionIds.togglePanel);

    protected override void Execute(EditorActionArgumentContext<EditorPanelId> context)
        => _ = context.interactions.TogglePanel(context.argument);
}
