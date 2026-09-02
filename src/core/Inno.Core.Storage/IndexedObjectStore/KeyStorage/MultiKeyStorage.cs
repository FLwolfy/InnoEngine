using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

internal sealed class MultiKeyStorage<TKey, T> : IKeyStorage<TKey, T> where T : class where TKey : notnull
{
    private readonly Dictionary<TKey, HashSet<T>> m_map = new(EqualityComparer<TKey>.Default);
    private static readonly HashSet<T> EMPTY_SET = new(IndexedObjectStore<T>.ReferenceEqualityComparer<T>.INSTANCE);

    /// <summary>
    /// Adds the supplied value while preserving the collection's invariants.
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

    /// <summary>
    /// Removes the requested value while preserving the collection's invariants.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    public void Remove(TKey key, T item)
    {
        if (!m_map.TryGetValue(key, out var set))
            return;

        set.Remove(item);
        if (set.Count == 0)
            m_map.Remove(key);
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

    /// <summary>
    /// Retrieves the requested count value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCount(TKey key)
        => m_map.TryGetValue(key, out var set) ? set.Count : 0;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TKey key, T item)
        => m_map.TryGetValue(key, out var set) && set.Contains(item);

    /// <summary>
    /// Determines whether a key currently has no indexed values.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsKeyEmpty(TKey key)
        => !m_map.TryGetValue(key, out var set) || set.Count == 0;

    /// <summary>
    /// Retrieves the requested set value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The validated hash sett? that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(TKey key)
        => m_map.TryGetValue(key, out var set) ? set : EMPTY_SET;

    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
        => m_map.Clear();
}
