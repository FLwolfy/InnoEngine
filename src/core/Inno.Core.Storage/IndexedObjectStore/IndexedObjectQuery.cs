using System;
using System.Collections.Generic;

namespace Inno.Core.Storage;

/// <summary>
/// Query builder over store keys.
/// </summary>
/// <typeparam name="T">The stored reference type.</typeparam>
public sealed class IndexedObjectQuery<T> where T : class
{
    private readonly IndexedObjectStore<T> m_store;
    private readonly List<IIndexedObjectQueryCondition<T>> m_conditions = new();
    private Func<IEnumerable<T>>? m_orderedFastExec;
    private Func<IReadOnlyList<T>>? m_orderedSnapshotExec;
    private Func<T?>? m_orderedFirstExec;

    internal IndexedObjectQuery(IndexedObjectStore<T> store)
    {
        m_store = store;
    }

    /// <summary>
    /// Adds a custom condition implementation.
    /// </summary>
    /// <param name="condition">The condition to append to this query.</param>
    /// <returns>The same query instance.</returns>
    public IndexedObjectQuery<T> Where(IIndexedObjectQueryCondition<T> condition)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        m_conditions.Add(condition);
        return this;
    }

    /// <summary>
    /// Adds a predicate condition evaluated against candidate items.
    /// </summary>
    /// <param name="predicate">Predicate to evaluate.</param>
    /// <returns>The same query instance.</returns>
    public IndexedObjectQuery<T> Where(Func<T, bool> predicate)
        => Where(new IndexedObjectPredicateCondition<T>(predicate));
    
    /// <summary>
    /// Adds a key equality condition.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <param name="value">Value to match.</param>
    /// <returns>The same query instance.</returns>
    public IndexedObjectQuery<T> Find<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull => Where(new IndexedObjectKeyCondition<T, TKey>(key, value));

    /// <summary>
    /// Orders results by a key marked with <see cref="IndexedObjectKeyFlags.Ordered"/>.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <returns>The same query instance.</returns>
    public IndexedObjectQuery<T> OrderBy<TKey>(IndexedObjectKey<TKey> key) where TKey : notnull
    {
        if (!m_store.IsOrderedKey(key))
            throw new InvalidOperationException($"Key '{key.name}' is not ordered.");

        m_orderedFastExec = () => m_store.ExecuteOrderedQueryFast(key, m_conditions);
        m_orderedSnapshotExec = () => m_store.ExecuteOrderedQuerySnapshot(key, m_conditions);
        m_orderedFirstExec = () => m_store.ExecuteOrderedFirst(key, m_conditions);
        return this;
    }

    /// <summary>
    /// Executes the query and returns a lazy fail-fast enumerable of results.
    /// </summary>
    /// <remarks>
    /// Enumeration is fail-fast and throws if the store is modified during iteration.
    /// </remarks>
    /// <returns>Lazy fail-fast enumerable of matching items.</returns>
    public IEnumerable<T> GetFast()
        => m_orderedFastExec != null ? m_orderedFastExec() : m_store.ExecuteQueryFast(m_conditions);

    /// <summary>
    /// Executes the query and returns a stable snapshot.
    /// </summary>
    /// <returns>A snapshot list detached from subsequent store mutations.</returns>
    public IReadOnlyList<T> Get()
        => m_orderedSnapshotExec != null ? m_orderedSnapshotExec() : m_store.ExecuteQuerySnapshot(m_conditions);

    /// <summary>
    /// Executes the query and returns the first matching item or null.
    /// </summary>
    /// <returns>The first matching item or null.</returns>
    public T? First()
        => m_orderedFirstExec != null ? m_orderedFirstExec() : m_store.ExecuteFirst(m_conditions);
}
