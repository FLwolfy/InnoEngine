using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Storage;

/// <summary>
/// Internal pool validation surface for PoolKey.
/// </summary>
internal interface IObjectPool
{
    bool IsValidKey(int id, Type keyType);
}

/// <summary>
/// Thread-safe object pool with optional query keys over stored items.
/// </summary>
public sealed class ObjectPool<T> : IObjectPool where T : class
{
    private readonly WeakReference<IObjectPool> m_poolRef;
    private readonly List<T> m_activeList = new();
    private readonly Dictionary<T, int> m_activeIndex = new(ReferenceEqualityComparer<T>.INSTANCE);
    private readonly Dictionary<int, IPoolIndex<T>> m_indexes = new();
    private readonly Dictionary<string, int> m_indexByName = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
    private int m_nextIndexId = 1;
    private int m_version;

    public ObjectPool()
    {
        m_poolRef = new WeakReference<IObjectPool>(this);
    }

    /// <summary>
    /// Number of stored items.
    /// </summary>
    public int count
    {
        get
        {
            m_lock.EnterReadLock();
            try
            {
                return m_activeList.Count;
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Clears all stored items and removes all defined keys.
    /// </summary>
    /// <remarks>
    /// After calling this, all key handles are invalid and must be redefined via <see cref="DefineKey{TKey}"/>.
    /// </remarks>
    public void Clear()
    {
        m_lock.EnterWriteLock();
        try
        {
            m_activeList.Clear();
            m_activeIndex.Clear();
            m_indexes.Clear();
            m_indexByName.Clear();
            m_nextIndexId = 1;
            BumpVersion();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Clears all stored items but keeps defined keys.
    /// </summary>
    /// <remarks>
    /// Keys remain valid but all indexed values are removed.
    /// </remarks>
    public void RemoveAll()
    {
        m_lock.EnterWriteLock();
        try
        {
            m_activeList.Clear();
            m_activeIndex.Clear();
            foreach (var index in m_indexes.Values)
                index.Clear();
            BumpVersion();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Defines a query key over stored items.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="name">Unique key name.</param>
    /// <param name="flags">Key behavior flags.</param>
    /// <param name="orderComparer">Optional order comparer. Required when <see cref="PoolKeyFlags.Ordered"/> is set.</param>
    /// <returns>The created key handle.</returns>
    public PoolKey<TKey> DefineKey<TKey>(
        string name,
        PoolKeyFlags flags = PoolKeyFlags.Unordered,
        IComparer<TKey>? orderComparer = null) where TKey : notnull
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        m_lock.EnterWriteLock();
        try
        {
            if (m_indexByName.TryGetValue(name, out _))
                throw new InvalidOperationException($"Key '{name}' already exists.");

            if ((flags & PoolKeyFlags.Ordered) != 0 && orderComparer == null)
                throw new ArgumentNullException(nameof(orderComparer), $"{nameof(orderComparer)} cannot be null when {nameof(flags)} is set to {nameof(PoolKeyFlags.Ordered)}.)");

            var index = new PoolIndex<T, TKey>(name, flags, orderComparer);
            var id = m_nextIndexId++;
            m_indexes[id] = index;
            m_indexByName[name] = id;
            BumpVersion();

            return new PoolKey<TKey>(m_poolRef, id, name);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a key previously defined on the pool.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">The key handle to remove.</param>
    /// <returns>True if removed.</returns>
    public bool RemoveKey<TKey>(PoolKey<TKey> key)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_indexes.Remove(key.id, out var removed))
                return false;

            m_indexByName.Remove(removed.name);
            BumpVersion();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets a previously defined key by name.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="name">Key name.</param>
    /// <param name="key">The resolved key handle.</param>
    /// <returns>True if found and type matches.</returns>
    public bool TryGetKey<TKey>(string name, out PoolKey<TKey> key) where TKey : notnull
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        m_lock.EnterReadLock();
        try
        {
            if (!m_indexByName.TryGetValue(name, out var id))
            {
                key = default;
                return false;
            }

            if (!m_indexes.TryGetValue(id, out var index) || index.keyType != typeof(TKey))
            {
                key = default;
                return false;
            }

            key = new PoolKey<TKey>(m_poolRef, id, name);
            return true;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    bool IObjectPool.IsValidKey(int id, Type keyType)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_indexes.TryGetValue(id, out var index) && index.keyType == keyType;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all defined key names as a lazy enumerable.
    /// </summary>
    /// <remarks>
    /// Enumeration is fail-fast and throws if the pool is modified during iteration.
    /// </remarks>
    /// <returns>Lazy enumerable of key names.</returns>
    public IEnumerable<string> GetAllKeys()
        => EnumerateKeys();

    private IEnumerable<string> EnumerateKeys()
    {
        var version = Volatile.Read(ref m_version);
        foreach (var name in m_indexByName.Keys)
        {
            EnsureVersion(version);
            yield return name;
        }
    }

    /// <summary>
    /// Adds an item to the pool without indexing.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public PoolEntry<T> Add(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.ContainsKey(item))
            {
                m_activeIndex[item] = m_activeList.Count;
                m_activeList.Add(item);
                BumpVersion();
            }
        }
        finally
        {
            m_lock.ExitWriteLock();
        }

        return new PoolEntry<T>(this, item);
    }

    /// <summary>
    /// Removes an item from the pool and all keys.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns>True if removed.</returns>
    public bool Remove(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.TryGetValue(item, out var index))
                return false;

            foreach (var poolIndex in m_indexes.Values)
                poolIndex.Remove(item);

            var lastIndex = m_activeList.Count - 1;
            var lastItem = m_activeList[lastIndex];
            m_activeList.RemoveAt(lastIndex);
            m_activeIndex.Remove(item);

            if (index != lastIndex)
            {
                m_activeList[index] = lastItem;
                m_activeIndex[lastItem] = index;
            }

            BumpVersion();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsValidItem(T item)
    {
        if (item == null) return false;

        m_lock.EnterReadLock();
        try
        {
            return m_activeIndex.ContainsKey(item);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Finds items by key and returns a lazy enumerable.
    /// </summary>
    /// <remarks>
    /// Enumeration is fail-fast and throws if the pool is modified during iteration.
    /// </remarks>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">The key handle to query.</param>
    /// <param name="value">The key value to look up.</param>
    /// <returns>Lazy enumerable of matching items.</returns>
    public IEnumerable<T> Find<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull
    {
        var index = GetIndex(key);
        return EnumerateFind(index, value);
    }

    private IEnumerable<T> EnumerateFind<TKey>(PoolIndex<T, TKey> index, TKey value) where TKey : notnull
    {
        var version = Volatile.Read(ref m_version);
        if ((index.flags & PoolKeyFlags.Unique) != 0 && index.TryGetSingle(value, out var single) && single != null)
        {
            EnsureVersion(version);
            yield return single;
            yield break;
        }

        var set = index.FindUnsafe(value);
        if (set == null || set.Count == 0)
            yield break;

        foreach (var item in set)
        {
            EnsureVersion(version);
            yield return item;
        }
    }

    /// <summary>
    /// Returns the first item by key or null if none exists.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">The key handle to query.</param>
    /// <param name="value">The key value to look up.</param>
    /// <returns>The first matching item or null.</returns>
    public T? First<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull
    {
        m_lock.EnterReadLock();
        try
        {
            if (GetIndex(key).TryGetSingle(value, out var item))
                return item;
            return null;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Starts a query over stored items.
    /// </summary>
    /// <returns>A query builder.</returns>
    public PoolQuery<T> Query()
        => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsOrderedKey<TKey>(PoolKey<TKey> key) where TKey : notnull
        => (GetIndex(key).flags & PoolKeyFlags.Ordered) != 0;

    private PoolIndex<T, TKey> GetIndex<TKey>(PoolKey<TKey> handle) where TKey : notnull
    {
        if (handle.poolRef == null || !handle.poolRef.TryGetTarget(out var owner) || !ReferenceEquals(owner, this))
            throw new InvalidOperationException($"Key '{handle.name}' does not belong to this pool.");

        if (!m_indexes.TryGetValue(handle.id, out var index))
            throw new InvalidOperationException($"Key '{handle.name}' not found.");

        if (index.keyType != typeof(TKey))
            throw new InvalidOperationException($"Key '{handle.name}' type mismatch.");

        return (PoolIndex<T, TKey>)index;
    }

    internal void SetKey<TKey>(T item, PoolKey<TKey> key, TKey value) where TKey : notnull
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.ContainsKey(item))
                throw new InvalidOperationException("Item is not in the pool.");

            GetIndex(key).AddOrUpdate(item, value);
            BumpVersion();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCountUnsafe<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull => GetIndex(key).GetCount(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetSingleUnsafe<TKey>(PoolKey<TKey> key, TKey value, out T? item) where TKey : notnull => GetIndex(key).TryGetSingle(value, out item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HashSet<T>? GetSetUnsafe<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull => GetIndex(key).FindUnsafe(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ContainsInSet<TKey>(PoolKey<TKey> key, TKey value, T item) where TKey : notnull => GetIndex(key).Contains(value, item);

    internal IEnumerable<T> ExecuteQuery(List<IQueryCondition<T>> conditions)
        => EnumerateQuery(conditions);

    private IEnumerable<T> EnumerateQuery(List<IQueryCondition<T>> conditions)
    {
        var version = Volatile.Read(ref m_version);
        if (conditions.Count == 0)
        {
            foreach (var item in m_activeList)
            {
                EnsureVersion(version);
                yield return item;
            }
            yield break;
        }

        int seedIndex = 0;
        int seedCount = conditions[0].GetCandidateCount(this);

        for (int i = 1; i < conditions.Count; i++)
        {
            var candidateCount = conditions[i].GetCandidateCount(this);
            if (candidateCount < seedCount)
            {
                seedCount = candidateCount;
                seedIndex = i;
            }
        }

        if (seedCount == 0)
            yield break;

        if (seedCount == 1 && conditions[seedIndex].TryGetSingle(this, out var single))
        {
            bool ok = true;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!conditions[i].Validate(this, single))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                yield return single;

            yield break;
        }

        var seed = conditions[seedIndex].GetSet(this);
        if (seed == null)
        {
            foreach (var item in m_activeList)
            {
                bool ok = true;
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (!conditions[i].Validate(this, item))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    EnsureVersion(version);
                    yield return item;
                }
            }

            yield break;
        }

        if (seed.Count == 0)
            yield break;

        foreach (var item in seed)
        {
            bool ok = true;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!conditions[i].Validate(this, item))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                EnsureVersion(version);
                yield return item;
            }
        }
    }

    internal T? ExecuteFirst(List<IQueryCondition<T>> conditions)
    {
        m_lock.EnterReadLock();
        try
        {
            if (conditions.Count == 0)
                return m_activeList.Count > 0 ? m_activeList[0] : null;

            int seedIndex = 0;
            int seedCount = conditions[0].GetCandidateCount(this);

            for (int i = 1; i < conditions.Count; i++)
            {
                var candidateCount = conditions[i].GetCandidateCount(this);
                if (candidateCount < seedCount)
                {
                    seedCount = candidateCount;
                    seedIndex = i;
                }
            }

            if (seedCount == 0)
                return null;

            if (seedCount == 1 && conditions[seedIndex].TryGetSingle(this, out var single))
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (!conditions[i].Validate(this, single))
                        return null;
                }

                return single;
            }

            var seed = conditions[seedIndex].GetSet(this);
            if (seed == null)
            {
                foreach (var item in m_activeList)
                {
                    bool ok = true;
                    for (int i = 0; i < conditions.Count; i++)
                    {
                        if (!conditions[i].Validate(this, item))
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                        return item;
                }

                return null;
            }

            if (seed.Count == 0)
                return null;

            foreach (var item in seed)
            {
                bool ok = true;
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (!conditions[i].Validate(this, item))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    return item;
            }

            return null;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    internal IEnumerable<T> ExecuteOrderedQuery<TKey>(
        PoolKey<TKey> orderKey,
        List<IQueryCondition<T>> conditions) where TKey : notnull
        => EnumerateOrderedQuery(orderKey, conditions);

    private IEnumerable<T> EnumerateOrderedQuery<TKey>(
        PoolKey<TKey> orderKey,
        List<IQueryCondition<T>> conditions) where TKey : notnull
    {
        var version = Volatile.Read(ref m_version);
        var index = GetIndex(orderKey);
        if ((index.flags & PoolKeyFlags.Ordered) == 0)
            throw new InvalidOperationException($"Key '{orderKey.name}' is not ordered.");

        foreach (var key in index.EnumerateOrderedKeys())
        {
            EnsureVersion(version);
            if (index.TryGetSingle(key, out var single) && single != null)
            {
                bool ok = true;
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (!conditions[i].Validate(this, single))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    yield return single;

                continue;
            }

            var set = index.FindUnsafe(key);
            if (set == null || set.Count == 0)
                continue;

            foreach (var item in set)
            {
                bool ok = true;
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (!conditions[i].Validate(this, item))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    EnsureVersion(version);
                    yield return item;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BumpVersion()
        => Interlocked.Increment(ref m_version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureVersion(int expected)
    {
        if (Volatile.Read(ref m_version) != expected)
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    internal T? ExecuteOrderedFirst<TKey>(
        PoolKey<TKey> orderKey,
        List<IQueryCondition<T>> conditions) where TKey : notnull
    {
        m_lock.EnterReadLock();
        try
        {
            var index = GetIndex(orderKey);
            if ((index.flags & PoolKeyFlags.Ordered) == 0)
                throw new InvalidOperationException($"Key '{orderKey.name}' is not ordered.");

            foreach (var key in index.EnumerateOrderedKeys())
            {
                if (index.TryGetSingle(key, out var single) && single != null)
                {
                    bool ok = true;
                    for (int i = 0; i < conditions.Count; i++)
                    {
                        if (!conditions[i].Validate(this, single))
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                        return single;

                    continue;
                }

                var set = index.FindUnsafe(key);
                if (set == null || set.Count == 0)
                    continue;

                foreach (var item in set)
                {
                    bool ok = true;
                    for (int i = 0; i < conditions.Count; i++)
                    {
                        if (!conditions[i].Validate(this, item))
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                        return item;
                }
            }

            return null;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    internal sealed class ReferenceEqualityComparer<TItem> : IEqualityComparer<TItem> where TItem : class
    {
        public static readonly ReferenceEqualityComparer<TItem> INSTANCE = new();

        public bool Equals(TItem? x, TItem? y) => ReferenceEquals(x, y);

        public int GetHashCode(TItem obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
