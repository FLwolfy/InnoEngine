namespace Inno.Editor.Interactions;

/// <summary>
/// Exposes reload-safe Undo and Redo operations based exclusively on neutral history changes.
/// </summary>
public interface IEditorHistory
{
    /// <summary>Gets whether an Undo transition is currently available.</summary>
    bool canUndo { get; }

    /// <summary>Gets whether a Redo transition is currently available.</summary>
    bool canRedo { get; }

    /// <summary>Gets whether a failed compensation left the domain state indeterminate.</summary>
    bool isFaulted { get; }

    /// <summary>Gets the next Undo operation name, or <see langword="null"/>.</summary>
    string? undoName { get; }

    /// <summary>Gets the next Redo operation name, or <see langword="null"/>.</summary>
    string? redoName { get; }

    /// <summary>Gets why the next Undo entry is unavailable, or <see langword="null"/>.</summary>
    string? undoUnavailableReason { get; }

    /// <summary>Gets why the next Redo entry is unavailable, or <see langword="null"/>.</summary>
    string? redoUnavailableReason { get; }

    /// <summary>Gets the diagnostic that faulted this history, or <see langword="null"/>.</summary>
    string? faultReason { get; }

    /// <summary>Gets the resident payload bytes retained by committed entries.</summary>
    long residentBytes { get; }

    /// <summary>Gets the temporary disk payload bytes retained by committed entries.</summary>
    long diskBytes { get; }

    /// <summary>Begins an atomic group of neutral history operations.</summary>
    /// <param name="name">The user-facing grouped operation name.</param>
    /// <returns>A transaction that must be committed or rolled back.</returns>
    EditorHistoryTransaction BeginTransaction(string name);

    /// <summary>Applies and records a neutral change through its current-generation handler.</summary>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="change">The independently owned neutral change.</param>
    /// <returns>The result of applying the change in the Redo direction.</returns>
    EditorHistoryResult Execute(string name, EditorHistoryChange change);

    /// <summary>Records a neutral change whose domain mutation is already applied.</summary>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="change">The independently owned neutral change.</param>
    void RecordApplied(string name, EditorHistoryChange change);

    /// <summary>Attempts to restore the state preceding the newest committed operation.</summary>
    /// <returns>The transition result. A state-preserving failure remains available for retry.</returns>
    EditorHistoryResult Undo();

    /// <summary>Attempts to reapply the newest reverted operation.</summary>
    /// <returns>The transition result. A state-preserving failure remains available for retry.</returns>
    EditorHistoryResult Redo();
}
