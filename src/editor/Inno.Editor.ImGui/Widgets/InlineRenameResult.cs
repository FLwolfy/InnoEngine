namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Describes the outcome of an inline rename control.
/// </summary>
public enum InlineRenameResult
{
    /// <summary>
    /// Indicates that the interaction remains active.
    /// </summary>
    None,

    /// <summary>
    /// Indicates that the edited text should be committed.
    /// </summary>
    Commit,

    /// <summary>
    /// Indicates that the edited text should be discarded.
    /// </summary>
    Cancel
}
