using System.Runtime.CompilerServices;

namespace Inno.Core.Storage;

/// <summary>
/// Entry wrapper for setting key values on a stored item.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public readonly struct IndexedObjectEntry<T> where T : class
{
    private readonly IndexedObjectStore<T> m_store;
    private readonly T m_item;

    internal IndexedObjectEntry(IndexedObjectStore<T> store, T item)
    {
        m_store = store;
        m_item = item;
    }

    /// <summary>
    /// Returns true if the entry is still valid in its owning store.
    /// </summary>
    public bool isValid => m_store.IsValidItem(m_item);

    /// <summary>
    /// Sets a key value for the stored item.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <param name="value">Key value.</param>
    /// <returns>The same entry for chaining.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IndexedObjectEntry<T> Set<TKey>(IndexedObjectKey<TKey> key, TKey value) where TKey : notnull
    {
        m_store.SetKey(m_item, key, value);
        return this;
    }
}
