using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

internal sealed class UniqueKeyStorage<TKey, T> : IKeyStorage<TKey, T> where T : class where TKey : notnull
{
    private readonly Dictionary<TKey, T> m_map = new(EqualityComparer<TKey>.Default);

    public bool Add(TKey key, T item)
    {
        if (m_map.TryGetValue(key, out var existing) && !ReferenceEquals(existing, item))
            throw new InvalidOperationException($"Duplicate key '{key}' in unique index.");

        m_map[key] = item;
        return true;
    }

    public void Remove(TKey key, T item)
    {
        if (m_map.TryGetValue(key, out var existing) && ReferenceEquals(existing, item))
            m_map.Remove(key);
    }

    public bool TryGetSingle(TKey key, out T? item)
        => m_map.TryGetValue(key, out item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCount(TKey key)
        => m_map.TryGetValue(key, out _) ? 1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TKey key, T item)
        => m_map.TryGetValue(key, out var existing) && ReferenceEquals(existing, item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsKeyEmpty(TKey key)
        => !m_map.TryGetValue(key, out _);

    public HashSet<T>? GetSet(TKey key)
        => null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
        => m_map.Clear();
}
