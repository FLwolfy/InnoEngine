namespace Inno.Editor.PlayMode;

/// <summary>
/// Identifies the current relationship between editor documents and game simulation.
/// </summary>
public enum EditorPlayModeState
{
    /// <summary>
    /// Editable scene documents are loaded and game simulation is stopped.
    /// </summary>
    Editing,

    /// <summary>
    /// Play Mode is waiting for the requested script generation to become active.
    /// </summary>
    Compiling,

    /// <summary>
    /// The active script generation is ready and isolated runtime state is being prepared.
    /// </summary>
    Preparing,

    /// <summary>
    /// Isolated runtime scenes are receiving the game update lifecycle.
    /// </summary>
    Playing,

    /// <summary>
    /// The isolated runtime session is stopping and releasing its owned state.
    /// </summary>
    Stopping,

    /// <summary>
    /// The most recent transition failed and its diagnostic remains available for inspection.
    /// </summary>
    Failed
}
