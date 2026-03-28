using System;
using System.Collections.Generic;

namespace Inno.Core.Storage;

/// <summary>
/// Query builder over pool keys.
/// </summary>
public sealed class PoolQuery<T> where T : class
{
    private readonly ObjectPool<T> m_pool;
    private readonly List<IQueryCondition<T>> m_conditions = new();
    private Func<IEnumerable<T>>? m_orderedFastExec;
    private Func<IReadOnlyList<T>>? m_orderedSnapshotExec;
    private Func<T?>? m_orderedFirstExec;

    internal PoolQuery(ObjectPool<T> pool)
    {
        m_pool = pool;
    }

    /// <summary>
    /// Adds a custom condition implementation.
    /// </summary>
    /// <param name="condition">QueryPredicate instance.</param>
    /// <returns>The same query instance.</returns>
    public PoolQuery<T> Where(IQueryCondition<T> condition)
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
    public PoolQuery<T> Where(Func<T, bool> predicate)
        => Where(new QueryPredicate<T>(predicate));
    
    /// <summary>
    /// Adds a key equality condition.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <param name="value">Value to match.</param>
    /// <returns>The same query instance.</returns>
    public PoolQuery<T> Find<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull => Where(new QueryFromKey<T, TKey>(key, value));

    /// <summary>
    /// Orders results by a key marked with <see cref="PoolKeyFlags.Ordered"/>.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <returns>The same query instance.</returns>
    public PoolQuery<T> OrderBy<TKey>(PoolKey<TKey> key) where TKey : notnull
    {
        if (!m_pool.IsOrderedKey(key))
            throw new InvalidOperationException($"Key '{key.name}' is not ordered.");

        m_orderedFastExec = () => m_pool.ExecuteOrderedQueryFast(key, m_conditions);
        m_orderedSnapshotExec = () => m_pool.ExecuteOrderedQuerySnapshot(key, m_conditions);
        m_orderedFirstExec = () => m_pool.ExecuteOrderedFirst(key, m_conditions);
        return this;
    }

    /// <summary>
    /// Executes the query and returns a lazy fail-fast enumerable of results.
    /// </summary>
    /// <remarks>
    /// Enumeration is fail-fast and throws if the pool is modified during iteration.
    /// </remarks>
    /// <returns>Lazy fail-fast enumerable of matching items.</returns>
    public IEnumerable<T> GetFast()
        => m_orderedFastExec != null ? m_orderedFastExec() : m_pool.ExecuteQueryFast(m_conditions);

    /// <summary>
    /// Executes the query and returns a stable snapshot.
    /// </summary>
    /// <returns>A snapshot list detached from subsequent pool mutations.</returns>
    public IReadOnlyList<T> Get()
        => m_orderedSnapshotExec != null ? m_orderedSnapshotExec() : m_pool.ExecuteQuerySnapshot(m_conditions);

    /// <summary>
    /// Executes the query and returns the first matching item or null.
    /// </summary>
    /// <returns>The first matching item or null.</returns>
    public T? First()
        => m_orderedFirstExec != null ? m_orderedFirstExec() : m_pool.ExecuteFirst(m_conditions);
}
