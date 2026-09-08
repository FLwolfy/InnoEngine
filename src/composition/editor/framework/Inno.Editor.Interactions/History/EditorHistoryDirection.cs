namespace Inno.Editor.Interactions;

/// <summary>
/// Identifies the direction in which an editor history change is being applied.
/// </summary>
public enum EditorHistoryDirection
{
    /// <summary>
    /// Restores the state that existed before the change was committed.
    /// </summary>
    Undo = 0,

    /// <summary>
    /// Restores the state produced by the committed change.
    /// </summary>
    Redo = 1
}
