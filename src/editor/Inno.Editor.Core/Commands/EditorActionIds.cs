namespace Inno.Editor.Core.Commands;

/// <summary>Defines stable identifiers for built-in editor actions.</summary>
public static class EditorActionIds
{
    /// <summary>Saves all open scene documents.</summary>
    public const string Save = "file.save";

    /// <summary>Opens the current target.</summary>
    public const string Open = "file.open";

    /// <summary>Selects the current target.</summary>
    public const string Select = "edit.select";

    /// <summary>Clears the current editor selection.</summary>
    public const string ClearSelection = "edit.clear-selection";

    /// <summary>Renames the current target.</summary>
    public const string Rename = "edit.rename";

    /// <summary>Deletes the current target.</summary>
    public const string Delete = "edit.delete";

    /// <summary>Resets a component or system.</summary>
    public const string Reset = "edit.reset";

    /// <summary>Removes a component or system.</summary>
    public const string Remove = "edit.remove";

    /// <summary>Toggles one editor panel.</summary>
    public const string TogglePanel = "window.toggle-panel";
}
