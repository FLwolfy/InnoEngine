using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

[Flags]
public enum PoolKeyFlags
{
    Unordered = 1 << 0,
    Ordered = 1 << 1,
    Unique =  1 << 2,
}

/// <summary>
/// Opaque handle used to query a key without exposing its implementation.
/// </summary>
/// <typeparam name="TKey">Key type.</typeparam>
public readonly struct PoolKey<TKey>
{
    internal readonly WeakReference<IObjectPool>? poolRef;
    internal readonly int id;
    
    /// <summary>
    /// Key name
    /// </summary>
    public readonly string name;
    
    /// <summary>
    /// Returns true if the pool key is still valid in its owning pool.
    /// </summary>
    public bool isValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (poolRef == null || !poolRef.TryGetTarget(out var pool))
                return false;

            return pool.IsValidKey(id, typeof(TKey));
        }
    }

    internal PoolKey(WeakReference<IObjectPool> poolRef, int id, string name)
    {
        this.poolRef = poolRef;
        this.id = id;
        this.name = name;
    }
}
