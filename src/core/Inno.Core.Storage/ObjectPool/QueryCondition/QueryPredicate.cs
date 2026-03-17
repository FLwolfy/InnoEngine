using System;
using System.Collections.Generic;

namespace Inno.Core.Storage;

public sealed class QueryPredicate<T> : IQueryCondition<T> where T : class
{
    private readonly Func<T, bool> m_predicate;

    public QueryPredicate(Func<T, bool> predicate)
    {
        m_predicate = predicate;
    }

    public int GetCandidateCount(ObjectPool<T> pool)
        => int.MaxValue;

    public bool TryGetSingle(ObjectPool<T> pool, out T item)
    {
        item = null!;
        return false;
    }

    public HashSet<T>? GetSet(ObjectPool<T> pool)
        => null;

    public bool Validate(ObjectPool<T> pool, T item)
        => m_predicate(item);

    public static implicit operator QueryPredicate<T>(Func<T, bool> predicate)
        => new(predicate);
}
