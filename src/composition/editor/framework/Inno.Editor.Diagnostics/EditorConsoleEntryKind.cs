namespace Inno.Editor.Diagnostics;

/// <summary>
/// Identifies the source protocol represented by one editor Console occurrence.
/// </summary>
public enum EditorConsoleEntryKind
{
    /// <summary>
    /// The occurrence was emitted through the runtime logging pipeline.
    /// </summary>
    Log,

    /// <summary>
    /// The occurrence represents current state from a diagnostic producer.
    /// </summary>
    Diagnostic
}
