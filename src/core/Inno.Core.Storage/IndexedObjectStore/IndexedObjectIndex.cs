using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

internal interface IIndexedObjectIndex<T> where T : class
{
    /// <summary>
    /// Gets the human-readable name used for presentation and diagnostics.
    /// </summary>
    string name { get; }
    /// <summary>
    /// Gets the key type accepted by this index implementation.
    /// </summary>
    Type keyType { get; }
    /// <summary>
    /// Adds a new keyed value or atomically replaces the existing value for that key.
    /// </summary>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    void AddOrUpdate(T item, object key);
    /// <summary>
    /// Removes the requested value while preserving the collection's invariants.
    /// </summary>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    void Remove(T item);
    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    void Clear();
    /// <summary>
    /// Retrieves the requested count value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    int GetCount(object key);
    /// <summary>
    /// Attempts to get single without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool TryGetSingle(object key, out T? item);
    /// <summary>
    /// Determines whether current state contains the requested value value.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool Contains(object key, T item);
    /// <summary>
    /// Retrieves the requested set value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The validated hash sett? that represents the completed operation.
    /// </returns>
    HashSet<T>? GetSet(object key);
}

internal sealed class IndexedObjectIndex<T, TKey> : IIndexedObjectIndex<T> where T : class where TKey : notnull
{
    private readonly IKeyStorage<TKey, T> m_storage;
    private readonly IKeyOrdering<TKey> m_ordering;
    private readonly IComparer<TKey> m_orderComparer;
    private readonly Dictionary<T, TKey> m_keyByItem;

    /// <summary>
    /// Gets the human-readable name used for presentation and diagnostics.
    /// </summary>
    public string name { get; }
    /// <summary>
    /// Gets the key type accepted by this index implementation.
    /// </summary>
    public Type keyType => typeof(TKey);
    /// <summary>
    /// Gets the index capabilities supported by this key declaration.
    /// </summary>
    public IndexedObjectKeyFlags flags { get; }

    internal IndexedObjectIndex(
        string name,
        IndexedObjectKeyFlags flags,
        IComparer<TKey>? orderComparer)
    {
        this.name = name;
        this.flags = flags;

        m_storage = (flags & IndexedObjectKeyFlags.Unique) != 0
            ? new UniqueKeyStorage<TKey, T>()
            : new MultiKeyStorage<TKey, T>();

        m_orderComparer = orderComparer ?? Comparer<TKey>.Default;
        m_ordering = (flags & IndexedObjectKeyFlags.Ordered) != 0
            ? new SortedKeyOrdering<TKey>(m_orderComparer)
            : new NullKeyOrdering<TKey>();

        m_keyByItem = new Dictionary<T, TKey>(IndexedObjectStore<T>.ReferenceEqualityComparer<T>.INSTANCE);
    }

    void IIndexedObjectIndex<T>.AddOrUpdate(T item, object key)
        => AddOrUpdate(item, (TKey)key);

    void IIndexedObjectIndex<T>.Remove(T item)
        => Remove(item);

    void IIndexedObjectIndex<T>.Clear()
        => Clear();

    int IIndexedObjectIndex<T>.GetCount(object key)
        => GetCount((TKey)key);

    bool IIndexedObjectIndex<T>.TryGetSingle(object key, out T? item)
        => TryGetSingle((TKey)key, out item);

    bool IIndexedObjectIndex<T>.Contains(object key, T item)
        => Contains((TKey)key, item);

    HashSet<T>? IIndexedObjectIndex<T>.GetSet(object key)
        => FindUnsafe((TKey)key);

    /// <summary>
    /// Adds a new keyed value or atomically replaces the existing value for that key.
    /// </summary>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    public void AddOrUpdate(T item, TKey key)
    {
        if (m_keyByItem.TryGetValue(item, out var oldKey))
        {
            if (EqualityComparer<TKey>.Default.Equals(oldKey, key))
                return;

            // Add first so a rejected unique key leaves the previous mapping intact.
            bool addedKey = m_storage.Add(key, item);
            m_storage.Remove(oldKey, item);
            if (m_storage.IsKeyEmpty(oldKey))
                m_ordering.RemoveKey(oldKey);
            if (addedKey)
                m_ordering.AddKey(key);
            m_keyByItem[item] = key;
            return;
        }

        if (m_storage.Add(key, item))
            m_ordering.AddKey(key);
        m_keyByItem[item] = key;
    }

    /// <summary>
    /// Removes the requested value while preserving the collection's invariants.
    /// </summary>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    public void Remove(T item)
    {
        if (!m_keyByItem.TryGetValue(item, out var key))
            return;

        m_keyByItem.Remove(item);
        m_storage.Remove(key, item);
        if (m_storage.IsKeyEmpty(key))
            m_ordering.RemoveKey(key);
    }

    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    public void Clear()
    {
        m_storage.Clear();
        m_ordering.Clear();
        m_keyByItem.Clear();
    }

    /// <summary>
    /// Attempts to get single without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSingle(TKey key, out T? item)
        => m_storage.TryGetSingle(key, out item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HashSet<T>? FindUnsafe(TKey key)
        => m_storage.GetSet(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCount(TKey key)
        => m_storage.GetCount(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(TKey key, T item)
        => m_storage.Contains(key, item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetKey(T item, out TKey key)
        => m_keyByItem.TryGetValue(item, out key!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CompareKeys(TKey left, TKey right)
        => m_orderComparer.Compare(left, right);

    internal IEnumerable<TKey> EnumerateOrderedKeys()
        => m_ordering.Enumerate();
}
