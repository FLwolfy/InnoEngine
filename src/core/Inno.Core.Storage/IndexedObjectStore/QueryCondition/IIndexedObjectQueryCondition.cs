using System.Collections.Generic;

namespace Inno.Core.Storage;

/// <summary>
/// Defines one condition evaluated by an object-store query.
/// </summary>
/// <typeparam name="T">
/// The stored reference type.
/// </typeparam>
public interface IIndexedObjectQueryCondition<T> where T : class
{
    /// <summary>
    /// Returns the estimated match count for this condition.
    /// </summary>
    /// <param name="store">
    /// Store being queried.
    /// </param>
    /// <returns>
    /// Estimated number of matches; use <see cref="int.MaxValue"/> when unknown.
    /// </returns>
    int GetCandidateCount(IndexedObjectStore<T> store);

    /// <summary>
    /// Tries to get a single matching item when possible.
    /// </summary>
    /// <param name="store">
    /// Store being queried.
    /// </param>
    /// <param name="item">
    /// The single matched item.
    /// </param>
    /// <returns>
    /// True when a single item is available.
    /// </returns>
    bool TryGetSingle(IndexedObjectStore<T> store, out T item);

    /// <summary>
    /// Returns a candidate set for this condition if available.
    /// </summary>
    /// <param name="store">
    /// Store being queried.
    /// </param>
    /// <returns>
    /// Candidate set, or null when not supported.
    /// </returns>
    HashSet<T>? GetSet(IndexedObjectStore<T> store);

    /// <summary>
    /// Checks whether the item satisfies this condition.
    /// </summary>
    /// <param name="store">
    /// Store being queried.
    /// </param>
    /// <param name="item">
    /// Item to test.
    /// </param>
    /// <returns>
    /// True if the item matches.
    /// </returns>
    bool Validate(IndexedObjectStore<T> store, T item);
}
