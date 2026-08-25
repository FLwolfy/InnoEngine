using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Settings;

internal static class SettingsInteractionIds
{
    internal const string C_MAIN_MENU_AREA = "editor/main-menu";
    internal const string C_OPEN = "editor.settings.open";

    internal static EditorAreaId mainMenuArea { get; } = new(C_MAIN_MENU_AREA);
    internal static EditorActionId open { get; } = new(C_OPEN);
}
