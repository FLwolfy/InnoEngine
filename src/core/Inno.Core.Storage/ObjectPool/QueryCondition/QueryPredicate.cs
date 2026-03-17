using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

public sealed class QueryPredicate<T> : IQueryCondition<T> where T : class
{
    private readonly Func<T, bool> m_predicate;

    public QueryPredicate(Func<T, bool> predicate)
    {
        m_predicate = predicate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCandidateCount(ObjectPool<T> pool)
        => int.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSingle(ObjectPool<T> pool, out T item)
    {
        item = null!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<T>? GetSet(ObjectPool<T> pool)
        => null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(ObjectPool<T> pool, T item)
        => m_predicate(item);

    public static implicit operator QueryPredicate<T>(Func<T, bool> predicate)
        => new(predicate);
}
