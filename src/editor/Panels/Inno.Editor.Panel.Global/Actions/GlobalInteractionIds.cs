using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

internal static class GlobalInteractionIds
{
    internal const string C_MAIN_MENU_AREA = "editor/main-menu";
    internal const string C_CLEAR_SELECTION = "editor/clear-selection";
    internal const string C_REDO = "editor/redo";
    internal const string C_SELECT = "editor/select";
    internal const string C_TOGGLE_PANEL = "editor/toggle-panel";
    internal const string C_UNDO = "editor/undo";
    internal const string C_ZOOM_IN = "editor.ui.zoom-in";
    internal const string C_ZOOM_OUT = "editor.ui.zoom-out";
    internal const string C_ZOOM_RESET = "editor.ui.zoom-reset";

    internal static EditorAreaId mainMenuArea { get; } = new(C_MAIN_MENU_AREA);
    internal static EditorActionId clearSelection { get; } = new(C_CLEAR_SELECTION);
    internal static EditorActionId redo { get; } = new(C_REDO);
    internal static EditorActionId select { get; } = new(C_SELECT);
    internal static EditorActionId togglePanel { get; } = new(C_TOGGLE_PANEL);
    internal static EditorActionId undo { get; } = new(C_UNDO);
    internal static EditorActionId zoomIn { get; } = new(C_ZOOM_IN);
    internal static EditorActionId zoomOut { get; } = new(C_ZOOM_OUT);
    internal static EditorActionId zoomReset { get; } = new(C_ZOOM_RESET);
}
