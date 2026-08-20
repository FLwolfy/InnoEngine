namespace Inno.Editor.Interactions.Actions;

/// <summary>Defines stable names for editor-wide action protocols.</summary>
public static class EditorActions
{
    /// <summary>Selects the current target.</summary>
    public const string Select = "editor/select";

    /// <summary>Clears the current editor selection.</summary>
    public const string ClearSelection = "editor/clear-selection";

    /// <summary>Opens the current target.</summary>
    public const string Open = "editor/open";

    /// <summary>Saves the current target.</summary>
    public const string Save = "editor/save";

    /// <summary>Deletes the current target.</summary>
    public const string Delete = "editor/delete";

    /// <summary>Toggles one editor panel.</summary>
    public const string TogglePanel = "editor/toggle-panel";
}
