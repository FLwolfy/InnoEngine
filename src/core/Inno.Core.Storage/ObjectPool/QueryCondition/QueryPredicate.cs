using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Matches object-pool items by evaluating a predicate.
/// </summary>
/// <typeparam name="T">The stored reference type.</typeparam>
public sealed class QueryPredicate<T> : IQueryCondition<T> where T : class
{
    private readonly Func<T, bool> m_predicate;

    /// <summary>Creates a predicate query condition.</summary>
    /// <param name="predicate">The predicate evaluated for each candidate.</param>
    public QueryPredicate(Func<T, bool> predicate)
    {
        m_predicate = predicate;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(ObjectPool<T> pool)
        => int.MaxValue;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSingle(ObjectPool<T> pool, out T item)
    {
        item = null!;
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(ObjectPool<T> pool)
        => null;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(ObjectPool<T> pool, T item)
        => m_predicate(item);

    /// <summary>Creates a query condition from a predicate delegate.</summary>
    /// <param name="predicate">The predicate to wrap.</param>
    public static implicit operator QueryPredicate<T>(Func<T, bool> predicate)
        => new(predicate);
}
