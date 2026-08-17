using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Engine.Scene;

/// <summary>Indexes concrete component buckets and assignable query views.</summary>
internal sealed class ComponentBucketRegistry
{
    private readonly Dictionary<Type, IComponentBucket> m_buckets = [];
    private readonly Dictionary<Type, IComponentBucket[]> m_assignableBuckets = [];

    internal IComponentBucket GetOrCreate(Type componentType)
    {
        if (m_buckets.TryGetValue(componentType, out IComponentBucket? existing))
            return existing;

        Type closedType = typeof(ComponentBucket<>).MakeGenericType(componentType);
        var created = (IComponentBucket)Activator.CreateInstance(closedType, nonPublic: true)!;
        m_buckets.Add(componentType, created);
        m_assignableBuckets.Clear();
        return created;
    }

    internal IReadOnlyList<IComponentBucket> GetAssignableTo(Type requestedType)
    {
        if (m_assignableBuckets.TryGetValue(requestedType, out IComponentBucket[]? cached))
            return cached;

        IComponentBucket[] result = m_buckets.Values
            .Where(bucket => requestedType.IsAssignableFrom(bucket.componentType))
            .OrderBy(bucket => bucket.componentType.FullName, StringComparer.Ordinal)
            .ToArray();
        m_assignableBuckets.Add(requestedType, result);
        return result;
    }

    internal void Clear()
    {
        foreach (IComponentBucket bucket in m_buckets.Values)
            bucket.Clear();
        m_buckets.Clear();
        m_assignableBuckets.Clear();
    }
}
