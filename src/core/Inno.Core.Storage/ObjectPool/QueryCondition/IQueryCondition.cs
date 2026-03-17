using System.Collections.Generic;

namespace Inno.Core.Storage;

public interface IQueryCondition<T> where T : class
{
    /// <summary>
    /// Returns the estimated match count for this condition.
    /// </summary>
    /// <param name="pool">Pool being queried.</param>
    /// <returns>Estimated number of matches; use <see cref="int.MaxValue"/> when unknown.</returns>
    int GetCandidateCount(ObjectPool<T> pool);

    /// <summary>
    /// Tries to get a single matching item when possible.
    /// </summary>
    /// <param name="pool">Pool being queried.</param>
    /// <param name="item">The single matched item.</param>
    /// <returns>True when a single item is available.</returns>
    bool TryGetSingle(ObjectPool<T> pool, out T item);

    /// <summary>
    /// Returns a candidate set for this condition if available.
    /// </summary>
    /// <param name="pool">Pool being queried.</param>
    /// <returns>Candidate set, or null when not supported.</returns>
    HashSet<T>? GetSet(ObjectPool<T> pool);

    /// <summary>
    /// Checks whether the item satisfies this condition.
    /// </summary>
    /// <param name="pool">Pool being queried.</param>
    /// <param name="item">Item to test.</param>
    /// <returns>True if the item matches.</returns>
    bool Validate(ObjectPool<T> pool, T item);
}
