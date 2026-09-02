namespace Inno.Editor.Interactions;

/// <summary>
/// Identifies a presentation-independent symbol for an editor toolbar command.
/// </summary>
public enum EditorToolbarIcon
{
    /// <summary>
    /// No symbol is requested.
    /// </summary>
    None,

    /// <summary>
    /// Starts an operation or simulation.
    /// </summary>
    Play,

    /// <summary>
    /// Stops an operation or simulation.
    /// </summary>
    Stop,

    /// <summary>
    /// Pauses an operation or simulation.
    /// </summary>
    Pause,

    /// <summary>
    /// Advances a paused operation by one step.
    /// </summary>
    Step,

    /// <summary>
    /// Returns to an editing state.
    /// </summary>
    Edit
}
