namespace Inno.Editor.Interactions;

internal static class EditorBuiltInInteractionIds
{
    internal const string C_CLEAR_SELECTION = "editor/clear-selection";
    internal const string C_GLOBAL_AREA = "editor/global";
    internal const string C_SELECT = "editor/select";

    internal static EditorAreaId globalArea { get; } = new(C_GLOBAL_AREA);
    internal static EditorCommand clearSelectionCommand { get; } =
        new(new EditorActionId(C_CLEAR_SELECTION));
    internal static EditorCommand selectCommand { get; } =
        new(new EditorActionId(C_SELECT));
}
