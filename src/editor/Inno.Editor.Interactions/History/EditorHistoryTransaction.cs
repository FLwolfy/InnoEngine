using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Collects several history operations into one atomic undo and redo entry.
/// </summary>
public sealed class EditorHistoryTransaction : IDisposable
{
    private EditorHistory? m_owner;
    private bool m_completed;

    internal EditorHistoryTransaction(EditorHistory owner, string name, Guid id)
    {
        m_owner = owner;
        this.name = name;
        this.id = id;
    }

    /// <summary>
    /// Gets the user-facing name assigned to the transaction.
    /// </summary>
    public string name { get; }

    internal Guid id { get; }

    /// <summary>
    /// Commits all recorded child operations as one atomic history entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction is no longer active.
    /// </exception>
    public void Commit()
    {
        EditorHistory owner = GetOwner();
        owner.CommitTransaction(this);
        Complete();
    }

    /// <summary>
    /// Reverts every operation recorded by the transaction and does not add a history entry.
    /// </summary>
    /// <returns>
    /// The result of the atomic rollback attempt.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction is no longer active.
    /// </exception>
    public EditorHistoryResult Rollback()
    {
        EditorHistory owner = GetOwner();
        EditorHistoryResult result = owner.RollbackTransaction(this);
        if (result.succeeded)
            Complete();
        else if (!result.statePreserved)
            Complete();
        return result;
    }

    /// <summary>
    /// Rolls back an uncommitted transaction before releasing it.
    /// </summary>
    public void Dispose()
    {
        if (m_completed)
            return;
        EditorHistory? owner = m_owner;
        if (owner is null)
        {
            Complete();
            return;
        }

        EditorHistoryResult result = owner.RollbackTransaction(this);
        if (result.succeeded)
        {
            Complete();
            GC.SuppressFinalize(this);
            return;
        }

        if (result.statePreserved)
        {
            owner.CommitTransaction(this);
            Complete();
            GC.SuppressFinalize(this);
            throw new InvalidOperationException(
                $"Transaction '{name}' could not roll back and was committed to Undo instead: {result.message}");
        }

        Complete();
        GC.SuppressFinalize(this);
        throw new InvalidOperationException(
            $"Transaction '{name}' lost state integrity while rolling back. The history is faulted: {result.message}");
    }

    private EditorHistory GetOwner()
        => !m_completed && m_owner is not null
            ? m_owner
            : throw new InvalidOperationException("The editor history transaction is no longer active.");

    private void Complete()
    {
        m_completed = true;
        m_owner = null;
    }
}
