namespace Inno.Editor.Diagnostics;

/// <summary>
/// Exposes read-only editor Console snapshots and explicit retention controls to editor features.
/// </summary>
public interface IEditorConsole
{
    /// <summary>
    /// Gets or sets the maximum number of ordinary log occurrences retained in memory.
    /// </summary>
    int capacity { get; set; }

    /// <summary>
    /// Gets or sets whether ordinary Console logs are cleared when a new Play Mode request begins.
    /// Current diagnostics remain visible because they represent active compiler and subsystem state.
    /// </summary>
    bool clearOnPlay { get; set; }

    /// <summary>
    /// Captures an immutable snapshot containing both individual occurrences and global groups.
    /// </summary>
    /// <returns>
    /// A consistent snapshot that remains valid while new entries arrive.
    /// </returns>
    EditorConsoleSnapshot Capture();

    /// <summary>
    /// Removes all retained logs and current diagnostic reports.
    /// </summary>
    void Clear();
}
