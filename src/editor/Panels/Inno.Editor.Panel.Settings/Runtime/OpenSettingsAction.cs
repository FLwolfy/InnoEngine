using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Settings;

[EditorAction(SettingsInteractionIds.C_OPEN)]
[EditorMenu(SettingsInteractionIds.C_MAIN_MENU_AREA, "Edit/Settings...", order: 1000)]
internal sealed class OpenSettingsAction(SettingsWindowModule window) : EditorAction
{
    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => window.Open();
}
