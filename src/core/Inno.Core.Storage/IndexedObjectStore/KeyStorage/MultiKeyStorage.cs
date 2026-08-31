using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

internal sealed class MultiKeyStorage<TKey, T> : IKeyStorage<TKey, T> where T : class where TKey : notnull
{
    private readonly Dictionary<TKey, HashSet<T>> m_map = new(EqualityComparer<TKey>.Default);
    private static readonly HashSet<T> EMPTY_SET = new(IndexedObjectStore<T>.ReferenceEqualityComparer<T>.INSTANCE);

    public bool Add(TKey key, T item)
    {
        if (!m_map.TryGetValue(key, out var set))
        {
            set = new HashSet<T>(IndexedObjectStore<T>.ReferenceEqualityComparer<T>.INSTANCE);
            m_map[key] = set;
        }

        var added = set.Add(item);
        return added && set.Count == 1;
    }

    public void Remove(TKey key, T item)
    {
        if (!m_map.TryGetValue(key, out var set))
            return;

        set.Remove(item);
        if (set.Count == 0)
            m_map.Remove(key);
    }

    public bool TryGetSingle(TKey key, out T? item)
    {
        item = null;
        if (!m_map.TryGetValue(key, out var set) || set.Count == 0)
            return false;

        using var e = set.GetEnumerator();
        if (!e.MoveNext())
            return false;

        item = e.Current;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCount(TKey key)
        => m_map.TryGetValue(key, out var set) ? set.Count : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TKey key, T item)
        => m_map.TryGetValue(key, out var set) && set.Contains(item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsKeyEmpty(TKey key)
        => !m_map.TryGetValue(key, out var set) || set.Count == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(TKey key)
        => m_map.TryGetValue(key, out var set) ? set : EMPTY_SET;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
        => m_map.Clear();
}
