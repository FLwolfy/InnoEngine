using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Defines indexing and cardinality behavior for an object-store key.
/// </summary>
[Flags]
public enum IndexedObjectKeyFlags
{
    /// <summary>
    /// Uses hash-based lookup without key ordering.
    /// </summary>
    Unordered = 1 << 0,
    /// <summary>
    /// Maintains key ordering using the supplied comparer.
    /// </summary>
    Ordered = 1 << 1,
    /// <summary>
    /// Allows at most one item for each key value.
    /// </summary>
    Unique =  1 << 2,
}

/// <summary>
/// Opaque handle used to query a key without exposing its implementation.
/// </summary>
/// <typeparam name="TKey">
/// Key type.
/// </typeparam>
public readonly struct IndexedObjectKey<TKey>
{
    internal readonly WeakReference<IIndexedObjectStore>? storeRef;
    internal readonly int id;
    
    /// <summary>
    /// Key name
    /// </summary>
    public readonly string name;
    
    /// <summary>
    /// Returns true if the store key is still valid in its owning store.
    /// </summary>
    public bool isValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (storeRef == null || !storeRef.TryGetTarget(out var store))
                return false;

            return store.IsValidKey(id, typeof(TKey));
        }
    }

    internal IndexedObjectKey(WeakReference<IIndexedObjectStore> storeRef, int id, string name)
    {
        this.storeRef = storeRef;
        this.id = id;
        this.name = name;
    }
}
