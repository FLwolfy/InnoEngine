using System;
using System.Collections.Generic;
using System.Diagnostics;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Owns the bounded, transactional Undo and Redo history for one editor runtime.
/// </summary>
internal sealed class EditorHistory : IEditorHistory, IDisposable
{
    private const int C_DEFAULT_CAPACITY = 256;

    private readonly List<EditorHistoryOperation> m_undo = [];
    private readonly List<EditorHistoryOperation> m_redo = [];
    private readonly Stack<TransactionOperation> m_transactions = [];
    private readonly EditorHistoryOptions m_options;
    private readonly EditorHistoryBlobStore m_blobStore;

    private IReadOnlyDictionary<string, EditorHistoryHandler> m_handlers =
        new Dictionary<string, EditorHistoryHandler>(StringComparer.Ordinal);
    private EditorHistoryContext? m_context;
    private bool m_isTransitioning;
    private bool m_isFaulted;
    private string? m_faultReason;
    private bool m_isDisposed;

    /// <summary>
    /// Creates an empty history with a bounded number of retained operations.
    /// </summary>
    /// <param name="capacity">The maximum number of committed top-level operations retained for Undo.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    internal EditorHistory(int capacity = C_DEFAULT_CAPACITY)
        : this(new EditorHistoryOptions { maxEntries = capacity })
    {
    }

    /// <summary>
    /// Creates an empty history with explicit entry, resident-memory, and temporary-disk budgets.
    /// </summary>
    /// <param name="options">The validated retention and payload storage options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an option contains an invalid capacity.</exception>
    internal EditorHistory(EditorHistoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        m_options = options;
        capacity = options.maxEntries;
        m_blobStore = new EditorHistoryBlobStore(options.cacheDirectory);
    }

    /// <summary>
    /// Gets the maximum number of committed top-level operations retained by this history.
    /// </summary>
    public int capacity { get; }

    /// <summary>
    /// Gets whether a previous operation is currently available and permits Undo.
    /// </summary>
    public bool canUndo => !m_isFaulted && !m_isTransitioning && m_transactions.Count == 0 &&
                           m_undo.Count > 0 && m_undo[^1].canUndo;

    /// <summary>
    /// Gets whether a reverted operation is currently available and permits Redo.
    /// </summary>
    public bool canRedo => !m_isFaulted && !m_isTransitioning && m_transactions.Count == 0 &&
                           m_redo.Count > 0 && m_redo[^1].canRedo;

    /// <summary>
    /// Gets whether a failed compensation left the domain state indeterminate.
    /// </summary>
    public bool isFaulted => m_isFaulted;

    /// <summary>
    /// Gets the diagnostic that faulted this history, or <see langword="null"/> while the history is healthy.
    /// </summary>
    public string? faultReason => m_faultReason;

    /// <summary>
    /// Gets the next operation name displayed by the Undo command, or <see langword="null"/> when unavailable.
    /// </summary>
    public string? undoName => m_undo.Count == 0 ? null : m_undo[^1].name;

    /// <summary>
    /// Gets the next operation name displayed by the Redo command, or <see langword="null"/> when unavailable.
    /// </summary>
    public string? redoName => m_redo.Count == 0 ? null : m_redo[^1].name;

    /// <summary>
    /// Gets the diagnostic explaining why the newest Undo entry is a barrier, or <see langword="null"/> when available.
    /// </summary>
    public string? undoUnavailableReason => GetUnavailableReason(m_undo, EditorHistoryDirection.Undo);

    /// <summary>
    /// Gets the diagnostic explaining why the newest Redo entry is a barrier, or <see langword="null"/> when available.
    /// </summary>
    public string? redoUnavailableReason => GetUnavailableReason(m_redo, EditorHistoryDirection.Redo);

    /// <summary>
    /// Gets the estimated resident payload bytes retained by committed Undo and Redo entries.
    /// </summary>
    public long residentBytes => Sum(m_undo, static operation => operation.estimatedMemorySize) +
                                 Sum(m_redo, static operation => operation.estimatedMemorySize);

    /// <summary>
    /// Gets the estimated temporary disk payload bytes retained by committed Undo and Redo entries.
    /// </summary>
    public long diskBytes => Sum(m_undo, static operation => operation.estimatedDiskSize) +
                             Sum(m_redo, static operation => operation.estimatedDiskSize);

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
    internal EditorHistoryResult Execute(
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
        {
            ObserveFailure(result);
            operation.Dispose();
        }
        return result;
    }

    /// <summary>
    /// Applies a neutral change through its current-generation handler and records it only when successful.
    /// </summary>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="change">The independently owned neutral change whose ownership transfers to the history.</param>
    /// <returns>The result of applying the change in the Redo direction.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change"/> is <see langword="null"/>.</exception>
    public EditorHistoryResult Execute(string name, EditorHistoryChange change)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(change);
        DataOperation operation;
        try
        {
            operation = new DataOperation(this, name, change);
        }
        finally
        {
            change.Dispose();
        }
        EditorHistoryResult result = Invoke(operation.RedoInternal, name, "execute");
        if (result.succeeded)
            Record(operation);
        else
        {
            ObserveFailure(result);
            operation.Dispose();
        }
        return result;
    }

    /// <summary>
    /// Records a mutation that has already been applied by the caller.
    /// </summary>
    /// <param name="operation">The complete reversible operation representing the applied state.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    internal void RecordApplied(EditorHistoryOperation operation)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(operation);
        Record(operation);
    }

    /// <summary>
    /// Records a neutral change whose mutation has already been applied by its feature facade.
    /// </summary>
    /// <param name="name">The user-facing operation name.</param>
    /// <param name="change">The independently owned neutral change whose ownership transfers to the history.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change"/> is <see langword="null"/>.</exception>
    public void RecordApplied(string name, EditorHistoryChange change)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(change);
        try
        {
            Record(new DataOperation(this, name, change));
        }
        finally
        {
            change.Dispose();
        }
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
    internal void RecordValue<T>(
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
    internal void Clear()
    {
        if (m_isDisposed)
            return;
        if (m_isTransitioning)
            throw new InvalidOperationException("Editor history cannot be cleared during Undo or Redo.");
        DisposeAll(m_undo);
        DisposeAll(m_redo);
        while (m_transactions.TryPop(out TransactionOperation? transaction))
            transaction.Dispose();
        m_isFaulted = false;
        m_faultReason = null;
    }

    /// <summary>
    /// Clears this history and releases every retained operation.
    /// </summary>
    public void Dispose()
    {
        if (m_isDisposed)
            return;
        Clear();
        m_blobStore.Dispose();
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
        TransactionOperation operation = PeekTransaction(transaction.id);
        EditorHistoryResult result = Invoke(operation.UndoInternal, operation.name, "rollback");
        if (result.succeeded)
        {
            _ = PopTransaction(transaction.id);
            operation.Dispose();
        }
        else
        {
            ObserveFailure(result);
        }
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
        EnforceBudgets();
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
            {
                ObserveFailure(result);
                return result;
            }
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

    private TransactionOperation PeekTransaction(Guid id)
    {
        if (!m_transactions.TryPeek(out TransactionOperation? operation) || operation.id != id)
            throw new InvalidOperationException("Editor history transactions must complete in stack order.");
        return operation;
    }

    private void EnsureMutable()
    {
        ObjectDisposedException.ThrowIf(m_isDisposed, this);
        if (m_isFaulted)
        {
            throw new InvalidOperationException(
                $"Editor history is faulted and must be cleared before further use. {m_faultReason}");
        }
        if (m_isTransitioning)
            throw new InvalidOperationException("History cannot be modified while Undo or Redo is executing.");
    }

    private void ObserveFailure(EditorHistoryResult result)
    {
        if (result.succeeded || result.statePreserved)
            return;
        m_isFaulted = true;
        m_faultReason = result.message;
        Log.Error("Editor history was faulted because state integrity was lost: {0}", result.message);
    }

    internal void Attach(EditorContext editor, EditorInteractions interactions)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(interactions);
        m_context = new EditorHistoryContext(editor, interactions);
    }

    internal HandlerUpdate PrepareHandlerUpdate(IReadOnlyDictionary<string, EditorHistoryHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return new HandlerUpdate(this, m_handlers, handlers);
    }

    private EditorHistoryChange RetainChange(EditorHistoryChange change)
        => change.Retain(
            m_blobStore,
            m_options.inlinePayloadThreshold,
            !string.IsNullOrWhiteSpace(m_options.cacheDirectory));

    private bool TryGetHandler(string kind, out EditorHistoryHandler? handler)
        => m_handlers.TryGetValue(kind, out handler);

    private EditorHistoryAvailability Query(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        if (m_context is null)
            return EditorHistoryAvailability.Unavailable("The editor history is not attached to an interaction runtime.");
        if (!TryGetHandler(change.kind, out EditorHistoryHandler? handler) || handler is null)
        {
            return EditorHistoryAvailability.Unavailable(
                $"Required history handler '{change.kind}' is not loaded.");
        }
        try
        {
            return handler.QueryInternal(m_context, change, direction);
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable(exception.Message);
        }
    }

    private EditorHistoryResult Apply(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        EditorHistoryAvailability availability = Query(change, direction);
        if (!availability.isAvailable)
            return EditorHistoryResult.Failure(availability.message);
        EditorHistoryHandler handler = m_handlers[change.kind];
        return handler.ApplyInternal(m_context!, change, direction);
    }

    private bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        if (!string.Equals(older.kind, newer.kind, StringComparison.Ordinal) ||
            older.mergeKey is null ||
            !string.Equals(older.mergeKey, newer.mergeKey, StringComparison.Ordinal) ||
            !TryGetHandler(older.kind, out EditorHistoryHandler? handler) ||
            handler is null)
        {
            return false;
        }
        return handler.TryMergeInternal(older, newer, out merged);
    }

    private void EnforceBudgets()
    {
        while (m_undo.Count > 0 &&
               (residentBytes > m_options.maxResidentBytes || diskBytes > m_options.maxDiskBytes))
        {
            m_undo[0].Dispose();
            m_undo.RemoveAt(0);
        }
    }

    private void DiscardRuntimeBoundEntries()
    {
        if (m_transactions.Count != 0)
        {
            while (m_transactions.TryPop(out TransactionOperation? transaction))
                transaction.Dispose();
        }

        int newestUnsafe = m_undo.FindLastIndex(static operation => !operation.isReloadSafe);
        if (newestUnsafe >= 0)
        {
            for (int i = newestUnsafe; i >= 0; i--)
                m_undo[i].Dispose();
            m_undo.RemoveRange(0, newestUnsafe + 1);
        }
        if (m_redo.Exists(static operation => !operation.isReloadSafe))
            DisposeAll(m_redo);
    }

    private string? GetUnavailableReason(
        IReadOnlyList<EditorHistoryOperation> operations,
        EditorHistoryDirection direction)
    {
        if (operations.Count == 0)
            return null;
        EditorHistoryOperation operation = operations[^1];
        if (operation is DataOperation data)
        {
            EditorHistoryAvailability availability = Query(data.change, direction);
            return availability.isAvailable ? null : availability.message;
        }
        bool available = direction == EditorHistoryDirection.Undo
            ? operation.canUndo
            : operation.canRedo;
        return available ? null : $"'{operation.name}' cannot currently be {direction.ToString().ToLowerInvariant()}.";
    }

    private static long Sum(
        IReadOnlyList<EditorHistoryOperation> operations,
        Func<EditorHistoryOperation, long> selector)
    {
        long total = 0L;
        for (int i = 0; i < operations.Count; i++)
            total = checked(total + selector(operations[i]));
        return total;
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
            return EditorHistoryResult.StateIntegrityLost(
                $"The {transition} callback for '{name}' threw before it could prove rollback: {exception.Message}");
        }
    }

    private static void DisposeAll(List<EditorHistoryOperation> operations)
    {
        for (int i = operations.Count - 1; i >= 0; i--)
            operations[i].Dispose();
        operations.Clear();
    }

    internal sealed class HandlerUpdate(
        EditorHistory owner,
        IReadOnlyDictionary<string, EditorHistoryHandler> previous,
        IReadOnlyDictionary<string, EditorHistoryHandler> candidate)
    {
        private bool m_activated;
        private bool m_finished;

        internal void Activate()
        {
            if (m_finished)
                throw new InvalidOperationException("History handler update is already finished.");
            if (m_activated)
                return;
            owner.m_handlers = candidate;
            m_activated = true;
        }

        internal void Rollback()
        {
            if (m_finished)
                return;
            if (m_activated)
                owner.m_handlers = previous;
            m_finished = true;
        }

        internal void Complete()
        {
            if (m_finished)
                return;
            if (!m_activated)
                throw new InvalidOperationException("History handler update has not been activated.");
            m_finished = true;
            try
            {
                owner.DiscardRuntimeBoundEntries();
            }
            catch (Exception exception)
            {
                Log.Error("Editor history could not release reload-unsafe entries: {0}", exception);
            }
        }
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

    private sealed class DataOperation : EditorHistoryOperation
    {
        private readonly EditorHistory m_owner;
        private readonly string m_name;
        private EditorHistoryChange m_change;

        internal DataOperation(EditorHistory owner, string operationName, EditorHistoryChange change)
        {
            m_owner = owner;
            m_name = operationName;
            m_change = owner.RetainChange(change);
        }

        public override string name => m_name;

        public override bool canUndo => m_owner.Query(m_change, EditorHistoryDirection.Undo).isAvailable;

        public override bool canRedo => m_owner.Query(m_change, EditorHistoryDirection.Redo).isAvailable;

        public override bool isReloadSafe => true;

        public override long estimatedMemorySize => m_change.residentSize;

        public override long estimatedDiskSize => m_change.diskSize;

        internal EditorHistoryChange change => m_change;

        protected override EditorHistoryResult Undo()
            => m_owner.Apply(m_change, EditorHistoryDirection.Undo);

        protected override EditorHistoryResult Redo()
            => m_owner.Apply(m_change, EditorHistoryDirection.Redo);

        protected override bool TryMerge(EditorHistoryOperation newer)
        {
            if (newer is not DataOperation candidate ||
                !m_owner.TryMerge(m_change, candidate.m_change, out EditorHistoryChange? merged) ||
                merged is null)
            {
                return false;
            }
            EditorHistoryChange retained;
            try
            {
                retained = m_owner.RetainChange(merged);
            }
            finally
            {
                merged.Dispose();
            }
            m_change.Dispose();
            m_change = retained;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_change.Dispose();
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

        public override bool isReloadSafe => m_children.TrueForAll(static operation => operation.isReloadSafe);

        public override bool canUndo => m_children.TrueForAll(static operation => operation.canUndo);

        public override bool canRedo => m_children.TrueForAll(static operation => operation.canRedo);

        public override long estimatedMemorySize
            => Sum(m_children, static operation => operation.estimatedMemorySize);

        public override long estimatedDiskSize
            => Sum(m_children, static operation => operation.estimatedDiskSize);

        internal void Add(EditorHistoryOperation operation) => m_children.Add(operation);

        protected override EditorHistoryResult Undo()
        {
            var compensationFailures = new List<string>();
            for (int i = m_children.Count - 1; i >= 0; i--)
            {
                EditorHistoryResult result = InvokeChild(m_children[i].UndoInternal, m_children[i].name, "undo");
                if (result.succeeded)
                    continue;
                for (int rollback = i + 1; rollback < m_children.Count; rollback++)
                {
                    EditorHistoryResult compensation = InvokeChild(
                        m_children[rollback].RedoInternal,
                        m_children[rollback].name,
                        "redo compensation");
                    if (!compensation.succeeded)
                        compensationFailures.Add($"'{m_children[rollback].name}': {compensation.message}");
                }
                return CombineFailure(result, compensationFailures, "undo");
            }
            return EditorHistoryResult.Success();
        }

        protected override EditorHistoryResult Redo()
        {
            var compensationFailures = new List<string>();
            for (int i = 0; i < m_children.Count; i++)
            {
                EditorHistoryResult result = InvokeChild(m_children[i].RedoInternal, m_children[i].name, "redo");
                if (result.succeeded)
                    continue;
                for (int rollback = i - 1; rollback >= 0; rollback--)
                {
                    EditorHistoryResult compensation = InvokeChild(
                        m_children[rollback].UndoInternal,
                        m_children[rollback].name,
                        "undo compensation");
                    if (!compensation.succeeded)
                        compensationFailures.Add($"'{m_children[rollback].name}': {compensation.message}");
                }
                return CombineFailure(result, compensationFailures, "redo");
            }
            return EditorHistoryResult.Success();
        }

        private static EditorHistoryResult InvokeChild(
            Func<EditorHistoryResult> callback,
            string childName,
            string transition)
        {
            try
            {
                return callback();
            }
            catch (Exception exception)
            {
                return EditorHistoryResult.StateIntegrityLost(
                    $"Child '{childName}' threw during {transition}: {exception.Message}");
            }
        }

        private static EditorHistoryResult CombineFailure(
            EditorHistoryResult original,
            IReadOnlyList<string> compensationFailures,
            string transition)
        {
            if (original.statePreserved && compensationFailures.Count == 0)
                return original;
            string compensation = compensationFailures.Count == 0
                ? "The failing child could not prove that it preserved its input state."
                : $"Compensation failures: {string.Join("; ", compensationFailures)}";
            return EditorHistoryResult.StateIntegrityLost(
                $"Transaction {transition} failed: {original.message} {compensation}");
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
