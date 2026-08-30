namespace Inno.Editor.PlayMode;

/// <summary>Identifies the current relationship between editor documents and game simulation.</summary>
public enum EditorPlayModeState
{
    /// <summary>Editable scene documents are loaded and game simulation is stopped.</summary>
    Editing,

    /// <summary>Play Mode is waiting for scripts and preparing isolated runtime scenes.</summary>
    EnteringPlay,

    /// <summary>Isolated runtime scenes are receiving the game update lifecycle.</summary>
    Playing,

    /// <summary>Runtime scenes are being discarded and editable state is being restored.</summary>
    ExitingPlay
}
