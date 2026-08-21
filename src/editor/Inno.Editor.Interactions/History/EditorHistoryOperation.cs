using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Represents one reversible editor mutation with independently validated undo and redo transitions.
/// </summary>
public abstract class EditorHistoryOperation : IDisposable
{
    private bool m_isDisposed;

    /// <summary>
    /// Gets the user-facing name shown by Undo and Redo commands.
    /// </summary>
    public abstract string name { get; }

    /// <summary>
    /// Gets whether the operation can currently transition to its previous state.
    /// </summary>
    public virtual bool canUndo => true;

    /// <summary>
    /// Gets whether the operation can currently transition to its next state.
    /// </summary>
    public virtual bool canRedo => true;

    internal EditorHistoryResult UndoInternal()
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        return canUndo
            ? Undo()
            : EditorHistoryResult.Failure($"'{name}' cannot currently be undone.");
    }

    internal EditorHistoryResult RedoInternal()
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        return canRedo
            ? Redo()
            : EditorHistoryResult.Failure($"'{name}' cannot currently be redone.");
    }

    internal bool TryMergeInternal(EditorHistoryOperation newer)
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        ArgumentNullException.ThrowIfNull(newer);
        return TryMerge(newer);
    }

    /// <summary>
    /// Restores the state that existed before this operation was applied.
    /// </summary>
    /// <returns>The result of the attempted transition.</returns>
    protected abstract EditorHistoryResult Undo();

    /// <summary>
    /// Restores the state produced when this operation was originally applied.
    /// </summary>
    /// <returns>The result of the attempted transition.</returns>
    protected abstract EditorHistoryResult Redo();

    /// <summary>
    /// Attempts to absorb a newer adjacent operation into this operation.
    /// </summary>
    /// <param name="newer">The operation recorded immediately after this operation.</param>
    /// <returns><see langword="true"/> when this operation now represents both mutations.</returns>
    protected virtual bool TryMerge(EditorHistoryOperation newer) => false;

    /// <summary>
    /// Releases resources retained exclusively for future undo and redo transitions.
    /// </summary>
    public void Dispose()
    {
        if (m_isDisposed)
            return;
        m_isDisposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources owned by a derived history operation.
    /// </summary>
    /// <param name="disposing">Whether managed resources may be released.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
