using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Storage;

/// <summary>
/// Internal store validation surface for IndexedObjectKey.
/// </summary>
internal interface IIndexedObjectStore
{
    /// <summary>
    /// Determines whether the key belongs to a live slot in the current storage generation.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="keyType">
    /// The key type consumed by is valid key; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool IsValidKey(int id, Type keyType);
}

/// <summary>
/// Thread-safe object store with optional query keys over stored items.
/// </summary>
/// <typeparam name="T">
/// The stored reference type.
/// </typeparam>
public sealed class IndexedObjectStore<T> : IIndexedObjectStore where T : class
{
    private readonly WeakReference<IIndexedObjectStore> m_storeRef;
    private readonly List<T> m_activeList = new();
    private readonly Dictionary<T, int> m_activeIndex = new(ReferenceEqualityComparer<T>.INSTANCE);
    private readonly Dictionary<T, IndexedObjectRuntimeHandle> m_handleByItem = new(ReferenceEqualityComparer<T>.INSTANCE);
    private readonly List<int> m_sparseToDense = new();
    private readonly List<uint> m_generations = new();
    private readonly List<int> m_denseToSlot = new();
    private Stack<int> m_freeSlots = new();
    private readonly Dictionary<int, IIndexedObjectIndex<T>> m_indexes = new();
    private readonly Dictionary<string, int> m_indexByName = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
    private int m_nextIndexId = 1;
    private int m_version;

    /// <summary>
    /// Creates an empty object store.
    /// </summary>
    public IndexedObjectStore()
    {
        m_storeRef = new WeakReference<IIndexedObjectStore>(this);
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
            m_handleByItem.Clear();
            m_sparseToDense.Clear();
            m_generations.Clear();
            m_denseToSlot.Clear();
            m_freeSlots = new Stack<int>();
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
            for (int i = 0; i < m_denseToSlot.Count; i++)
            {
                int slot = m_denseToSlot[i];
                m_sparseToDense[slot] = -1;
                m_generations[slot]++;
                m_freeSlots.Push(slot);
            }

            m_activeList.Clear();
            m_activeIndex.Clear();
            m_handleByItem.Clear();
            m_denseToSlot.Clear();
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
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="name">
    /// Unique key name.
    /// </param>
    /// <param name="flags">
    /// Key behavior flags.
    /// </param>
    /// <param name="orderComparer">
    /// Optional order comparer. Required when <see cref="IndexedObjectKeyFlags.Ordered"/> is set.
    /// </param>
    /// <returns>
    /// The created key handle.
    /// </returns>
    public IndexedObjectKey<TKey> DefineKey<TKey>(
        string name,
        IndexedObjectKeyFlags flags = IndexedObjectKeyFlags.Unordered,
        IComparer<TKey>? orderComparer = null) where TKey : notnull
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        m_lock.EnterWriteLock();
        try
        {
            if (m_indexByName.TryGetValue(name, out _))
                throw new InvalidOperationException($"Key '{name}' already exists.");

            if ((flags & IndexedObjectKeyFlags.Ordered) != 0 && orderComparer == null)
                throw new ArgumentNullException(nameof(orderComparer), $"{nameof(orderComparer)} cannot be null when {nameof(flags)} is set to {nameof(IndexedObjectKeyFlags.Ordered)}.)");

            var index = new IndexedObjectIndex<T, TKey>(name, flags, orderComparer);
            var id = m_nextIndexId++;
            m_indexes[id] = index;
            m_indexByName[name] = id;
            BumpVersion();

            return new IndexedObjectKey<TKey>(m_storeRef, id, name);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a key previously defined on the store.
    /// </summary>
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="key">
    /// The key handle to remove.
    /// </param>
    /// <returns>
    /// True if removed.
    /// </returns>
    public bool RemoveKey<TKey>(IndexedObjectKey<TKey> key)
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
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="name">
    /// Key name.
    /// </param>
    /// <param name="key">
    /// The resolved key handle.
    /// </param>
    /// <returns>
    /// True if found and type matches.
    /// </returns>
    public bool TryGetKey<TKey>(string name, out IndexedObjectKey<TKey> key) where TKey : notnull
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

            key = new IndexedObjectKey<TKey>(m_storeRef, id, name);
            return true;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    bool IIndexedObjectStore.IsValidKey(int id, Type keyType)
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
    /// Enumeration is fail-fast and throws if the store is modified during iteration.
    /// </remarks>
    /// <returns>
    /// Lazy enumerable of key names.
    /// </returns>
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
    /// Adds an item to the store without indexing.
    /// </summary>
    /// <param name="item">
    /// The item to add.
    /// </param>
    /// <returns>
    /// The validated indexed object entryt that represents the completed operation.
    /// </returns>
    public IndexedObjectEntry<T> Add(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.ContainsKey(item))
            {
                IndexedObjectRuntimeHandle handle = AllocateHandle();
                int denseIndex = m_activeList.Count;
                m_activeIndex[item] = denseIndex;
                m_handleByItem[item] = handle;
                m_activeList.Add(item);
                m_denseToSlot.Add(handle.slot);
                m_sparseToDense[handle.slot] = denseIndex;
                BumpVersion();
            }
        }
        finally
        {
            m_lock.ExitWriteLock();
        }

        return new IndexedObjectEntry<T>(this, item);
    }

    /// <summary>
    /// Removes an item from the store and all keys.
    /// </summary>
    /// <param name="item">
    /// The item to remove.
    /// </param>
    /// <returns>
    /// True if removed.
    /// </returns>
    public bool Remove(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.TryGetValue(item, out var index))
                return false;

            foreach (var objectIndex in m_indexes.Values)
                objectIndex.Remove(item);

            var lastIndex = m_activeList.Count - 1;
            var lastItem = m_activeList[lastIndex];
            var removedSlot = m_denseToSlot[index];
            var lastSlot = m_denseToSlot[lastIndex];
            m_activeList.RemoveAt(lastIndex);
            m_denseToSlot.RemoveAt(lastIndex);
            m_activeIndex.Remove(item);
            m_handleByItem.Remove(item);

            if (index != lastIndex)
            {
                m_activeList[index] = lastItem;
                m_activeIndex[lastItem] = index;
                m_denseToSlot[index] = lastSlot;
                m_sparseToDense[lastSlot] = index;
            }

            ReleaseSlot(removedSlot);
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
    /// Tries to get the runtime handle for an item currently stored in the store.
    /// </summary>
    /// <param name="item">
    /// Item to resolve.
    /// </param>
    /// <param name="handle">
    /// Resolved runtime handle when successful.
    /// </param>
    /// <returns>
    /// True when the item is currently stored in this store.
    /// </returns>
    internal bool TryGetHandle(T item, out IndexedObjectRuntimeHandle handle)
    {
        if (item == null)
        {
            handle = default;
            return false;
        }

        m_lock.EnterReadLock();
        try
        {
            return m_handleByItem.TryGetValue(item, out handle);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns true when the provided runtime handle still points to a live item in this store.
    /// </summary>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool IsHandleValid(IndexedObjectRuntimeHandle handle)
    {
        m_lock.EnterReadLock();
        try
        {
            return IsHandleValidNoLock(handle);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Tries to resolve an item by runtime handle.
    /// </summary>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool TryGetByHandle(IndexedObjectRuntimeHandle handle, out T? item)
    {
        m_lock.EnterReadLock();
        try
        {
            if (!IsHandleValidNoLock(handle))
            {
                item = null;
                return false;
            }

            int denseIndex = m_sparseToDense[handle.slot];
            item = m_activeList[denseIndex];
            return true;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Finds items by key and returns a lazy fail-fast enumerable.
    /// </summary>
    /// <remarks>
    /// Enumeration throws if the store is modified during iteration.
    /// </remarks>
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="key">
    /// The key handle to query.
    /// </param>
    /// <param name="value">
    /// The key value to look up.
    /// </param>
    /// <returns>
    /// Lazy fail-fast enumerable of matching items.
    /// </returns>
    public IEnumerable<T> FindFast<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull
    {
        var index = GetIndex(key);
        return EnumerateFind(index, value);
    }

    /// <summary>
    /// Finds items by key and returns a stable snapshot.
    /// </summary>
    /// <remarks>
    /// The returned list is detached from subsequent store mutations.
    /// </remarks>
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="key">
    /// The key handle to query.
    /// </param>
    /// <param name="value">
    /// The key value to look up.
    /// </param>
    /// <returns>
    /// A stable snapshot list of matching items.
    /// </returns>
    public IReadOnlyList<T> Find<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull
    {
        m_lock.EnterReadLock();
        try
        {
            var index = GetIndex(key);
            return BuildFindSnapshot(index, value);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    private IEnumerable<T> EnumerateFind<TKey>(IndexedObjectIndex<T, TKey> index, TKey value) where TKey : notnull
    {
        var version = Volatile.Read(ref m_version);
        if ((index.flags & IndexedObjectKeyFlags.Unique) != 0 && index.TryGetSingle(value, out var single) && single != null)
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

    private List<T> BuildFindSnapshot<TKey>(IndexedObjectIndex<T, TKey> index, TKey value) where TKey : notnull
    {
        var result = new List<T>();
        if ((index.flags & IndexedObjectKeyFlags.Unique) != 0 && index.TryGetSingle(value, out var single) && single != null)
        {
            result.Add(single);
            return result;
        }

        var set = index.FindUnsafe(value);
        if (set == null || set.Count == 0)
        {
            return result;
        }

        foreach (var item in set)
        {
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Returns the first item by key or null if none exists.
    /// </summary>
    /// <typeparam name="TKey">
    /// Key type.
    /// </typeparam>
    /// <param name="key">
    /// The key handle to query.
    /// </param>
    /// <param name="value">
    /// The key value to look up.
    /// </param>
    /// <returns>
    /// The first matching item or null.
    /// </returns>
    public T? First<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull
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
    /// Returns all stored items as a stable snapshot.
    /// </summary>
    /// <remarks>
    /// The returned list is detached from subsequent store mutations.
    /// </remarks>
    /// <returns>
    /// A snapshot list of all stored items.
    /// </returns>
    public IReadOnlyList<T> All()
    {
        m_lock.EnterReadLock();
        try
        {
            var result = new List<T>(m_activeList.Count);
            result.AddRange(m_activeList);
            return result;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns all stored items as a lazy fail-fast enumerable.
    /// </summary>
    /// <remarks>
    /// Enumeration throws if the store is modified during iteration.
    /// </remarks>
    /// <returns>
    /// Lazy fail-fast enumerable of all stored items.
    /// </returns>
    public IEnumerable<T> AllFast()
    {
        var version = Volatile.Read(ref m_version);
        foreach (var item in m_activeList)
        {
            EnsureVersion(version);
            yield return item;
        }
    }

    /// <summary>
    /// Starts a query over stored items.
    /// </summary>
    /// <returns>
    /// A query builder.
    /// </returns>
    public IndexedObjectQuery<T> Query()
        => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsOrderedKey<TKey>(IndexedObjectKey<TKey> key) where TKey : notnull
        => (GetIndex(key).flags & IndexedObjectKeyFlags.Ordered) != 0;

    private IndexedObjectIndex<T, TKey> GetIndex<TKey>(IndexedObjectKey<TKey> handle) where TKey : notnull
    {
        if (handle.storeRef == null || !handle.storeRef.TryGetTarget(out var owner) || !ReferenceEquals(owner, this))
            throw new InvalidOperationException($"Key '{handle.name}' does not belong to this store.");

        if (!m_indexes.TryGetValue(handle.id, out var index))
            throw new InvalidOperationException($"Key '{handle.name}' not found.");

        if (index.keyType != typeof(TKey))
            throw new InvalidOperationException($"Key '{handle.name}' type mismatch.");

        return (IndexedObjectIndex<T, TKey>)index;
    }

    internal void SetKey<TKey>(T item, IndexedObjectKey<TKey> key, TKey value) where TKey : notnull
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_activeIndex.ContainsKey(item))
                throw new InvalidOperationException("Item is not in the store.");

            GetIndex(key).AddOrUpdate(item, value);
            BumpVersion();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCountUnsafe<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull => GetIndex(key).GetCount(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetSingleUnsafe<TKey>(IndexedObjectKey<TKey> key, TKey value, out T? item) where TKey : notnull => GetIndex(key).TryGetSingle(value, out item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HashSet<T>? GetSetUnsafe<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull => GetIndex(key).FindUnsafe(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ContainsInSet<TKey>(IndexedObjectKey<TKey> key, TKey value, T item) where TKey : notnull => GetIndex(key).Contains(value, item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IndexedObjectRuntimeHandle AllocateHandle()
    {
        int slot;
        if (m_freeSlots.Count > 0)
        {
            slot = m_freeSlots.Pop();
        }
        else
        {
            slot = m_generations.Count;
            m_generations.Add(1);
            m_sparseToDense.Add(-1);
        }

        return new IndexedObjectRuntimeHandle(slot, m_generations[slot]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReleaseSlot(int slot)
    {
        m_sparseToDense[slot] = -1;
        m_generations[slot]++;
        m_freeSlots.Push(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsHandleValidNoLock(IndexedObjectRuntimeHandle handle)
    {
        int slot = handle.slot;
        if ((uint)slot >= (uint)m_generations.Count)
            return false;

        if (m_generations[slot] != handle.generation)
            return false;

        int denseIndex = m_sparseToDense[slot];
        return denseIndex >= 0 && denseIndex < m_activeList.Count;
    }

    internal IEnumerable<T> ExecuteQueryFast(List<IIndexedObjectQueryCondition<T>> conditions)
        => EnumerateQuery(conditions);

    internal IReadOnlyList<T> ExecuteQuerySnapshot(List<IIndexedObjectQueryCondition<T>> conditions)
    {
        m_lock.EnterReadLock();
        try
        {
            var result = new List<T>();
            if (conditions.Count == 0)
            {
                result.AddRange(m_activeList);
                return result;
            }

            int seedIndex = 0;
            int seedCount = conditions[0].GetCandidateCount(this);
            for (int i = 1; i < conditions.Count; i++)
            {
                int candidateCount = conditions[i].GetCandidateCount(this);
                if (candidateCount < seedCount)
                {
                    seedCount = candidateCount;
                    seedIndex = i;
                }
            }

            if (seedCount == 0)
            {
                return result;
            }

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
                {
                    result.Add(single);
                }

                return result;
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
                        result.Add(item);
                    }
                }

                return result;
            }

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
                    result.Add(item);
                }
            }

            return result;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    private IEnumerable<T> EnumerateQuery(List<IIndexedObjectQueryCondition<T>> conditions)
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

    internal T? ExecuteFirst(List<IIndexedObjectQueryCondition<T>> conditions)
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

    internal IEnumerable<T> ExecuteOrderedQueryFast<TKey>(
        IndexedObjectKey<TKey> orderKey,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
        => EnumerateOrderedQuery(orderKey, conditions);

    internal IReadOnlyList<T> ExecuteOrderedQuerySnapshot<TKey>(
        IndexedObjectKey<TKey> orderKey,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
    {
        m_lock.EnterReadLock();
        try
        {
            var index = GetIndex(orderKey);
            if ((index.flags & IndexedObjectKeyFlags.Ordered) == 0)
            {
                throw new InvalidOperationException($"Key '{orderKey.name}' is not ordered.");
            }

            if (TryBuildOrderedCandidates(index, conditions, out List<OrderedCandidate<TKey>>? candidates))
                return SelectOrderedItems(candidates);

            return ScanOrderedSnapshot(index, conditions);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    private IEnumerable<T> EnumerateOrderedQuery<TKey>(
        IndexedObjectKey<TKey> orderKey,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
    {
        var version = Volatile.Read(ref m_version);
        var index = GetIndex(orderKey);
        if ((index.flags & IndexedObjectKeyFlags.Ordered) == 0)
            throw new InvalidOperationException($"Key '{orderKey.name}' is not ordered.");

        if (TryBuildOrderedCandidates(index, conditions, out List<OrderedCandidate<TKey>>? candidates))
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                EnsureVersion(version);
                yield return candidates[i].item;
            }
            yield break;
        }

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
        IndexedObjectKey<TKey> orderKey,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
    {
        m_lock.EnterReadLock();
        try
        {
            var index = GetIndex(orderKey);
            if ((index.flags & IndexedObjectKeyFlags.Ordered) == 0)
                throw new InvalidOperationException($"Key '{orderKey.name}' is not ordered.");

            if (TryGetOrderedCandidateSeed(conditions, out int seedIndex, out int seedCount))
            {
                if (seedCount == 0)
                    return null;
                if (seedCount == 1 && conditions[seedIndex].TryGetSingle(this, out T single))
                {
                    return ValidateConditions(conditions, single) ? single : null;
                }

                HashSet<T>? seed = conditions[seedIndex].GetSet(this);
                if (seed is not null)
                {
                    T? first = null;
                    TKey firstKey = default!;
                    int firstDenseIndex = int.MaxValue;
                    foreach (T item in seed)
                    {
                        if (!ValidateConditions(conditions, item) ||
                            !index.TryGetKey(item, out TKey itemKey) ||
                            !m_activeIndex.TryGetValue(item, out int denseIndex))
                        {
                            continue;
                        }

                        int comparison = first is null ? -1 : index.CompareKeys(itemKey, firstKey);
                        if (comparison < 0 || comparison == 0 && denseIndex < firstDenseIndex)
                        {
                            first = item;
                            firstKey = itemKey;
                            firstDenseIndex = denseIndex;
                        }
                    }
                    return first;
                }
            }

            return ScanOrderedFirst(index, conditions);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    private bool TryBuildOrderedCandidates<TKey>(
        IndexedObjectIndex<T, TKey> orderIndex,
        List<IIndexedObjectQueryCondition<T>> conditions,
        out List<OrderedCandidate<TKey>> candidates) where TKey : notnull
    {
        candidates = [];
        if (!TryGetOrderedCandidateSeed(conditions, out int seedIndex, out int seedCount))
            return false;
        if (seedCount == 0)
            return true;

        if (seedCount == 1 && conditions[seedIndex].TryGetSingle(this, out T single))
        {
            AddOrderedCandidate(orderIndex, conditions, candidates, single);
            return true;
        }

        HashSet<T>? seed = conditions[seedIndex].GetSet(this);
        if (seed is null)
            return false;
        foreach (T item in seed)
            AddOrderedCandidate(orderIndex, conditions, candidates, item);
        SortOrderedCandidates(orderIndex, candidates);
        return true;
    }

    private bool TryGetOrderedCandidateSeed(
        List<IIndexedObjectQueryCondition<T>> conditions,
        out int seedIndex,
        out int seedCount)
    {
        seedIndex = -1;
        seedCount = int.MaxValue;
        for (int i = 0; i < conditions.Count; i++)
        {
            int candidateCount = conditions[i].GetCandidateCount(this);
            if (candidateCount >= seedCount)
                continue;
            seedCount = candidateCount;
            seedIndex = i;
        }
        return seedIndex >= 0 && seedCount != int.MaxValue;
    }

    private void AddOrderedCandidate<TKey>(
        IndexedObjectIndex<T, TKey> orderIndex,
        List<IIndexedObjectQueryCondition<T>> conditions,
        List<OrderedCandidate<TKey>> candidates,
        T item) where TKey : notnull
    {
        if (ValidateConditions(conditions, item) &&
            orderIndex.TryGetKey(item, out TKey itemKey) &&
            m_activeIndex.TryGetValue(item, out int denseIndex))
        {
            candidates.Add(new OrderedCandidate<TKey>(item, itemKey, denseIndex));
        }
    }

    private static IReadOnlyList<T> SelectOrderedItems<TKey>(
        List<OrderedCandidate<TKey>> candidates) where TKey : notnull
    {
        var result = new T[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            result[i] = candidates[i].item;
        return result;
    }

    private IReadOnlyList<T> ScanOrderedSnapshot<TKey>(
        IndexedObjectIndex<T, TKey> index,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
    {
        var result = new List<T>();
        foreach (TKey key in index.EnumerateOrderedKeys())
        {
            if (index.TryGetSingle(key, out T? single) && single is not null)
            {
                if (ValidateConditions(conditions, single))
                    result.Add(single);
                continue;
            }

            HashSet<T>? set = index.FindUnsafe(key);
            if (set is null)
                continue;
            foreach (T item in set)
            {
                if (ValidateConditions(conditions, item))
                    result.Add(item);
            }
        }
        return result;
    }

    private T? ScanOrderedFirst<TKey>(
        IndexedObjectIndex<T, TKey> index,
        List<IIndexedObjectQueryCondition<T>> conditions) where TKey : notnull
    {
        foreach (TKey key in index.EnumerateOrderedKeys())
        {
            if (index.TryGetSingle(key, out T? single) && single is not null)
            {
                if (ValidateConditions(conditions, single))
                    return single;
                continue;
            }

            HashSet<T>? set = index.FindUnsafe(key);
            if (set is null)
                continue;
            foreach (T item in set)
            {
                if (ValidateConditions(conditions, item))
                    return item;
            }
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ValidateConditions(List<IIndexedObjectQueryCondition<T>> conditions, T item)
    {
        for (int i = 0; i < conditions.Count; i++)
        {
            if (!conditions[i].Validate(this, item))
                return false;
        }
        return true;
    }

    private static void SortOrderedCandidates<TKey>(
        IndexedObjectIndex<T, TKey> index,
        List<OrderedCandidate<TKey>> candidates) where TKey : notnull
        => candidates.Sort((left, right) =>
        {
            int comparison = index.CompareKeys(left.key, right.key);
            return comparison != 0
                ? comparison
                : left.denseIndex.CompareTo(right.denseIndex);
        });

    private readonly record struct OrderedCandidate<TKey>(T item, TKey key, int denseIndex)
        where TKey : notnull;

    internal sealed class ReferenceEqualityComparer<TItem> : IEqualityComparer<TItem> where TItem : class
    {
        /// <summary>
        /// The instance value used as part of this type's public representation.
        /// </summary>
        public static readonly ReferenceEqualityComparer<TItem> INSTANCE = new();

        /// <summary>
        /// Determines whether this value and the supplied value represent the same logical state.
        /// </summary>
        /// <param name="x">
        /// The horizontal or first component.
        /// </param>
        /// <param name="y">
        /// The vertical or second component.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
        public bool Equals(TItem? x, TItem? y) => ReferenceEquals(x, y);

        /// <summary>
        /// Computes a hash code consistent with the implemented equality contract.
        /// </summary>
        /// <param name="obj">
        /// The object compared with this value.
        /// </param>
        /// <returns>
        /// The scalar result calculated from the supplied inputs.
        /// </returns>
        public int GetHashCode(TItem obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
