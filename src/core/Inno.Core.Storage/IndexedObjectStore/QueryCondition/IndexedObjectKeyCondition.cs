using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Matches object-store items associated with one indexed key value.
/// </summary>
/// <typeparam name="T">
/// The stored reference type.
/// </typeparam>
/// <typeparam name="TKey">
/// The indexed key type.
/// </typeparam>
public sealed class IndexedObjectKeyCondition<T, TKey> : IIndexedObjectQueryCondition<T> where T : class where TKey : notnull
{
    private readonly IndexedObjectKey<TKey> m_key;
    private readonly TKey m_value;

    /// <summary>
    /// Creates an indexed-key query condition.
    /// </summary>
    /// <param name="key">
    /// The store key to query.
    /// </param>
    /// <param name="value">
    /// The required key value.
    /// </param>
    public IndexedObjectKeyCondition(IndexedObjectKey<TKey> key, TKey value)
    {
        m_key = key;
        m_value = value;
    }

    /// <summary>
    /// Gets a candidate count required by the implemented contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    /// <param name="store">
    /// The store consumed by get candidate count; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(IndexedObjectStore<T> store)
        => store.GetCountUnsafe(m_key, m_value);

    /// <summary>
    /// Attempts to get single without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="store">
    /// The store consumed by try get single; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
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

    /// <summary>
    /// Gets a set required by the implemented contract.
    /// </summary>
    /// <returns>
    /// The validated hash sett? that represents the completed operation.
    /// </returns>
    /// <param name="store">
    /// The store consumed by get set; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(IndexedObjectStore<T> store)
        => store.GetSetUnsafe(m_key, m_value);

    /// <summary>
    /// Validates the supplied input and rejects state that cannot satisfy this contract.
    /// </summary>
    /// <param name="store">
    /// The store consumed by validate; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeds or its condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(IndexedObjectStore<T> store, T item)
        => store.ContainsInSet(m_key, m_value, item);
}
