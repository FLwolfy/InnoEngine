using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

internal interface IIndexedObjectIndex<T> where T : class
{
    string name { get; }
    Type keyType { get; }
    void AddOrUpdate(T item, object key);
    void Remove(T item);
    void Clear();
    int GetCount(object key);
    bool TryGetSingle(object key, out T? item);
    bool Contains(object key, T item);
    HashSet<T>? GetSet(object key);
}

internal sealed class IndexedObjectIndex<T, TKey> : IIndexedObjectIndex<T> where T : class where TKey : notnull
{
    private readonly IKeyStorage<TKey, T> m_storage;
    private readonly IKeyOrdering<TKey> m_ordering;
    private readonly Dictionary<T, TKey> m_keyByItem;

    public string name { get; }
    public Type keyType => typeof(TKey);
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

        m_ordering = (flags & IndexedObjectKeyFlags.Ordered) != 0
            ? new SortedKeyOrdering<TKey>(orderComparer ?? Comparer<TKey>.Default)
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

    public void Remove(T item)
    {
        if (!m_keyByItem.TryGetValue(item, out var key))
            return;

        m_keyByItem.Remove(item);
        m_storage.Remove(key, item);
        if (m_storage.IsKeyEmpty(key))
            m_ordering.RemoveKey(key);
    }

    public void Clear()
    {
        m_storage.Clear();
        m_ordering.Clear();
        m_keyByItem.Clear();
    }

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

    internal IEnumerable<TKey> EnumerateOrderedKeys()
        => m_ordering.Enumerate();
}
