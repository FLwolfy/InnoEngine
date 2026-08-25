using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Matches object-store items associated with one indexed key value.
/// </summary>
/// <typeparam name="T">The stored reference type.</typeparam>
/// <typeparam name="TKey">The indexed key type.</typeparam>
public sealed class IndexedObjectKeyCondition<T, TKey> : IIndexedObjectQueryCondition<T> where T : class where TKey : notnull
{
    private readonly IndexedObjectKey<TKey> m_key;
    private readonly TKey m_value;

    /// <summary>Creates an indexed-key query condition.</summary>
    /// <param name="key">The store key to query.</param>
    /// <param name="value">The required key value.</param>
    public IndexedObjectKeyCondition(IndexedObjectKey<TKey> key, TKey value)
    {
        m_key = key;
        m_value = value;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(IndexedObjectStore<T> store)
        => store.GetCountUnsafe(m_key, m_value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSingle(IndexedObjectStore<T> store, out T item)
    {
        if (store.TryGetSingleUnsafe(m_key, m_value, out var found) && found != null)
        {
            item = found;
            return true;
        }

        item = null!;
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(IndexedObjectStore<T> store)
        => store.GetSetUnsafe(m_key, m_value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(IndexedObjectStore<T> store, T item)
        => store.ContainsInSet(m_key, m_value, item);
}
