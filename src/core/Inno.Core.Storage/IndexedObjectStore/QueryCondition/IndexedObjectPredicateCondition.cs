using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Matches object-store items by evaluating a predicate.
/// </summary>
/// <typeparam name="T">
/// The stored reference type.
/// </typeparam>
public sealed class IndexedObjectPredicateCondition<T> : IIndexedObjectQueryCondition<T> where T : class
{
    private readonly Func<T, bool> m_predicate;

    /// <summary>
    /// Creates a predicate query condition.
    /// </summary>
    /// <param name="predicate">
    /// The predicate evaluated for each candidate.
    /// </param>
    public IndexedObjectPredicateCondition(Func<T, bool> predicate)
    {
        m_predicate = predicate;
    }

    /// <summary>
    /// Gets a candidate count required by the implemented contract.
    /// </summary>
    /// <param name="store">
    /// The store consumed by get candidate count; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(IndexedObjectStore<T> store)
        => int.MaxValue;

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
        item = null!;
        return false;
    }

    /// <summary>
    /// Gets a set required by the implemented contract.
    /// </summary>
    /// <param name="store">
    /// The store consumed by get set; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated hash sett? that represents the completed operation.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(IndexedObjectStore<T> store)
        => null;

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
        => m_predicate(item);

    /// <summary>
    /// Creates a query condition from a predicate delegate.
    /// </summary>
    /// <param name="predicate">
    /// The predicate to wrap.
    /// </param>
    /// <returns>
    /// The validated indexed object predicate conditiont that represents the completed operation.
    /// </returns>
    public static implicit operator IndexedObjectPredicateCondition<T>(Func<T, bool> predicate)
        => new(predicate);
}
