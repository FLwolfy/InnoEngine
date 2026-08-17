using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Matches object-pool items associated with one indexed key value.
/// </summary>
/// <typeparam name="T">The stored reference type.</typeparam>
/// <typeparam name="TKey">The indexed key type.</typeparam>
public sealed class QueryFromKey<T, TKey> : IQueryCondition<T> where T : class where TKey : notnull
{
    private readonly PoolKey<TKey> m_key;
    private readonly TKey m_value;

    /// <summary>Creates an indexed-key query condition.</summary>
    /// <param name="key">The pool key to query.</param>
    /// <param name="value">The required key value.</param>
    public QueryFromKey(PoolKey<TKey> key, TKey value)
    {
        m_key = key;
        m_value = value;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(ObjectPool<T> pool)
        => pool.GetCountUnsafe(m_key, m_value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSingle(ObjectPool<T> pool, out T item)
    {
        if (pool.TryGetSingleUnsafe(m_key, m_value, out var found) && found != null)
        {
            item = found;
            return true;
        }

        item = null!;
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(ObjectPool<T> pool)
        => pool.GetSetUnsafe(m_key, m_value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(ObjectPool<T> pool, T item)
        => pool.ContainsInSet(m_key, m_value, item);
}
