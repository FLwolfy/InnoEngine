using System;
using System.Collections.Generic;
using System.Diagnostics;

using Inno.Core.Logging;

namespace Inno.Editor.Interactions;

/// <summary>
/// Owns the bounded, transactional Undo and Redo history for one editor runtime.
/// </summary>
public sealed class EditorHistory : IDisposable
{
    private const int C_DEFAULT_CAPACITY = 256;

    private readonly List<EditorHistoryOperation> m_undo = [];
    private readonly List<EditorHistoryOperation> m_redo = [];
    private readonly Stack<TransactionOperation> m_transactions = [];
    private bool m_isTransitioning;
    private bool m_isDisposed;

    /// <summary>
    /// Creates an empty history with a bounded number of retained operations.
    /// </summary>
    /// <param name="capacity">The maximum number of committed top-level operations retained for Undo.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public EditorHistory(int capacity = C_DEFAULT_CAPACITY)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "History capacity must be positive.");
        this.capacity = capacity;
    }

    /// <summary>
    /// Gets the maximum number of committed top-level operations retained by this history.
    /// </summary>
    public int capacity { get; }

    /// <summary>
    /// Gets whether a previous operation is currently available and permits Undo.
    /// </summary>
    public bool canUndo => !m_isTransitioning && m_transactions.Count == 0 &&
                           m_undo.Count > 0 && m_undo[^1].canUndo;

    /// <summary>
    /// Gets whether a reverted operation is currently available and permits Redo.
    /// </summary>
    public bool canRedo => !m_isTransitioning && m_transactions.Count == 0 &&
                           m_redo.Count > 0 && m_redo[^1].canRedo;

    /// <summary>
    /// Gets the next operation name displayed by the Undo command, or <see langword="null"/> when unavailable.
    /// </summary>
    public string? undoName => m_undo.Count == 0 ? null : m_undo[^1].name;

    /// <summary>
    /// Gets the next operation name displayed by the Redo command, or <see langword="null"/> when unavailable.
    /// </summary>
    public string? redoName => m_redo.Count == 0 ? null : m_redo[^1].name;

    /// <summary>
    /// Begins an atomic group whose child operations appear as one Undo entry.
    /// </summary>
    /// <param name="name">The user-facing name of the grouped operation.</param>
    /// <returns>A transaction that must be committed or rolled back.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown while Undo or Redo is executing.</exception>
    public EditorHistoryTransaction BeginTransaction(string name)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Guid id = Guid.NewGuid();
        var operation = new TransactionOperation(name, id);
        m_transactions.Push(operation);
        return new EditorHistoryTransaction(this, name, id);
    }

    /// <summary>
    /// Executes a mutation and records its inverse only when the mutation succeeds.
    /// </summary>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="execute">The callback that applies the initial mutation and every future Redo.</param>
    /// <param name="undo">The callback that restores the previous state.</param>
    /// <param name="mergeKey">An optional stable key used to coalesce adjacent value-like edits.</param>
    /// <returns>The result produced by the initial mutation.</returns>
    public EditorHistoryResult Execute(
        string name,
        Func<EditorHistoryResult> execute,
        Func<EditorHistoryResult> undo,
        object? mergeKey = null)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(undo);
        var operation = new DelegateOperation(name, undo, execute, mergeKey);
        EditorHistoryResult result = Invoke(operation.RedoInternal, name, "execute");
        if (result.succeeded)
            Record(operation);
        else
            operation.Dispose();
        return result;
    }

    /// <summary>
    /// Records a mutation that has already been applied by the caller.
    /// </summary>
    /// <param name="operation">The complete reversible operation representing the applied state.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public void RecordApplied(EditorHistoryOperation operation)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(operation);
        Record(operation);
    }

    /// <summary>
    /// Records an already-applied value change with automatic adjacent-edit coalescing.
    /// </summary>
    /// <typeparam name="T">The value type captured by the operation.</typeparam>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="before">The value that existed before the edit.</param>
    /// <param name="after">The value produced by the edit.</param>
    /// <param name="apply">The callback that assigns either captured value.</param>
    /// <param name="mergeKey">A stable key identifying edits to the same logical value.</param>
    public void RecordValue<T>(
        string name,
        T before,
        T after,
        Action<T> apply,
        object mergeKey)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(mergeKey);
        Record(new ValueOperation<T>(name, before, after, apply, mergeKey));
    }

    /// <summary>
    /// Attempts to restore the state preceding the newest committed operation.
    /// </summary>
    /// <returns>The transition result. A failed operation remains available for retry.</returns>
    public EditorHistoryResult Undo() => Transition(m_undo, m_redo, undo: true);

    /// <summary>
    /// Attempts to reapply the newest reverted operation.
    /// </summary>
    /// <returns>The transition result. A failed operation remains available for retry.</returns>
    public EditorHistoryResult Redo() => Transition(m_redo, m_undo, undo: false);

    /// <summary>
    /// Removes and disposes every Undo and Redo operation.
    /// </summary>
    public void Clear()
    {
        if (m_isDisposed)
            return;
        if (m_isTransitioning)
            throw new InvalidOperationException("Editor history cannot be cleared during Undo or Redo.");
        DisposeAll(m_undo);
        DisposeAll(m_redo);
        while (m_transactions.TryPop(out TransactionOperation? transaction))
            transaction.Dispose();
    }

    /// <summary>
    /// Clears this history and releases every retained operation.
    /// </summary>
    public void Dispose()
    {
        if (m_isDisposed)
            return;
        Clear();
        m_isDisposed = true;
        GC.SuppressFinalize(this);
    }

    internal void CommitTransaction(EditorHistoryTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureMutable();
        TransactionOperation operation = PopTransaction(transaction.id);
        if (operation.count == 0)
        {
            operation.Dispose();
            return;
        }
        Record(operation);
    }

    internal EditorHistoryResult RollbackTransaction(EditorHistoryTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EnsureMutable();
        TransactionOperation operation = PopTransaction(transaction.id);
        EditorHistoryResult result = Invoke(operation.UndoInternal, operation.name, "rollback");
        operation.Dispose();
        return result;
    }

    private void Record(EditorHistoryOperation operation)
    {
        if (m_transactions.TryPeek(out TransactionOperation? transaction))
        {
            transaction.Add(operation);
            return;
        }

        DisposeAll(m_redo);
        if (m_undo.Count > 0 && m_undo[^1].TryMergeInternal(operation))
        {
            operation.Dispose();
            return;
        }
        m_undo.Add(operation);
        while (m_undo.Count > capacity)
        {
            m_undo[0].Dispose();
            m_undo.RemoveAt(0);
        }
    }

    private EditorHistoryResult Transition(
        List<EditorHistoryOperation> source,
        List<EditorHistoryOperation> destination,
        bool undo)
    {
        EnsureMutable();
        if (m_transactions.Count != 0)
            return EditorHistoryResult.Failure("Undo and Redo are unavailable while a transaction is active.");
        if (source.Count == 0)
            return EditorHistoryResult.Failure(undo ? "Nothing to undo." : "Nothing to redo.");

        EditorHistoryOperation operation = source[^1];
        m_isTransitioning = true;
        try
        {
            EditorHistoryResult result = Invoke(
                undo ? operation.UndoInternal : operation.RedoInternal,
                operation.name,
                undo ? "undo" : "redo");
            if (!result.succeeded)
                return result;
            source.RemoveAt(source.Count - 1);
            destination.Add(operation);
            return result;
        }
        finally
        {
            m_isTransitioning = false;
        }
    }

    private TransactionOperation PopTransaction(Guid id)
    {
        if (!m_transactions.TryPop(out TransactionOperation? operation) ||
            operation.id != id)
        {
            throw new InvalidOperationException("Editor history transactions must complete in stack order.");
        }
        return operation;
    }

    private void EnsureMutable()
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        if (m_isTransitioning)
            throw new InvalidOperationException("History cannot be modified while Undo or Redo is executing.");
    }

    private static EditorHistoryResult Invoke(
        Func<EditorHistoryResult> callback,
        string name,
        string transition)
    {
        try
        {
            EditorHistoryResult result = callback();
            if (!result.succeeded)
                Log.Warn("Editor history operation '{0}' could not {1}: {2}", name, transition, result.message);
            return result;
        }
        catch (Exception exception)
        {
            Log.Error("Editor history operation '{0}' failed to {1}: {2}", name, transition, exception);
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static void DisposeAll(List<EditorHistoryOperation> operations)
    {
        for (int i = operations.Count - 1; i >= 0; i--)
            operations[i].Dispose();
        operations.Clear();
    }

    private sealed class DelegateOperation : EditorHistoryOperation
    {
        private readonly Func<EditorHistoryResult> m_undo;
        private readonly object? m_mergeKey;
        private Func<EditorHistoryResult> m_redo;

        internal DelegateOperation(
            string operationName,
            Func<EditorHistoryResult> undo,
            Func<EditorHistoryResult> redo,
            object? mergeKey)
        {
            name = operationName;
            m_undo = undo;
            m_redo = redo;
            m_mergeKey = mergeKey;
        }

        public override string name { get; }

        protected override EditorHistoryResult Undo() => m_undo();

        protected override EditorHistoryResult Redo() => m_redo();

        protected override bool TryMerge(EditorHistoryOperation newer)
        {
            if (m_mergeKey is null || newer is not DelegateOperation candidate ||
                !Equals(m_mergeKey, candidate.m_mergeKey))
            {
                return false;
            }
            m_redo = candidate.m_redo;
            return true;
        }
    }

    private sealed class ValueOperation<T> : EditorHistoryOperation
    {
        private const double C_MERGE_WINDOW_SECONDS = 1.0;

        private readonly T m_before;
        private readonly Action<T> m_apply;
        private readonly object m_mergeKey;
        private T m_after;
        private long m_lastEditTimestamp;

        internal ValueOperation(
            string operationName,
            T before,
            T after,
            Action<T> apply,
            object mergeKey)
        {
            name = operationName;
            m_before = before;
            m_after = after;
            m_apply = apply;
            m_mergeKey = mergeKey;
            m_lastEditTimestamp = Stopwatch.GetTimestamp();
        }

        public override string name { get; }

        protected override EditorHistoryResult Undo()
        {
            m_apply(m_before);
            return EditorHistoryResult.Success();
        }

        protected override EditorHistoryResult Redo()
        {
            m_apply(m_after);
            return EditorHistoryResult.Success();
        }

        protected override bool TryMerge(EditorHistoryOperation newer)
        {
            if (newer is not ValueOperation<T> candidate ||
                !Equals(m_mergeKey, candidate.m_mergeKey) ||
                Stopwatch.GetElapsedTime(m_lastEditTimestamp, candidate.m_lastEditTimestamp).TotalSeconds >
                C_MERGE_WINDOW_SECONDS)
                return false;
            m_after = candidate.m_after;
            m_lastEditTimestamp = candidate.m_lastEditTimestamp;
            return true;
        }
    }

    private sealed class TransactionOperation(string operationName, Guid transactionId) : EditorHistoryOperation
    {
        private readonly List<EditorHistoryOperation> m_children = [];

        public override string name => operationName;

        internal Guid id => transactionId;

        internal int count => m_children.Count;

        internal void Add(EditorHistoryOperation operation) => m_children.Add(operation);

        protected override EditorHistoryResult Undo()
        {
            int undone = 0;
            for (int i = m_children.Count - 1; i >= 0; i--)
            {
                EditorHistoryResult result = m_children[i].UndoInternal();
                if (result.succeeded)
                {
                    undone++;
                    continue;
                }
                for (int rollback = i + 1; rollback < i + 1 + undone; rollback++)
                    _ = m_children[rollback].RedoInternal();
                return result;
            }
            return EditorHistoryResult.Success();
        }

        protected override EditorHistoryResult Redo()
        {
            int redone = 0;
            for (int i = 0; i < m_children.Count; i++)
            {
                EditorHistoryResult result = m_children[i].RedoInternal();
                if (result.succeeded)
                {
                    redone++;
                    continue;
                }
                for (int rollback = redone - 1; rollback >= 0; rollback--)
                    _ = m_children[rollback].UndoInternal();
                return result;
            }
            return EditorHistoryResult.Success();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;
            for (int i = m_children.Count - 1; i >= 0; i--)
                m_children[i].Dispose();
            m_children.Clear();
        }
    }
}
