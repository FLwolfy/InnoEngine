namespace Inno.Core.Storage;

/// <summary>
/// Entry wrapper for setting key values on a stored item.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public readonly struct PoolEntry<T> where T : class
{
    private readonly ObjectPool<T> m_pool;
    private readonly T m_item;

    internal PoolEntry(ObjectPool<T> pool, T item)
    {
        m_pool = pool;
        m_item = item;
    }

    /// <summary>
    /// Returns true if the entry is still valid in its owning pool.
    /// </summary>
    public bool isValid => m_pool.IsValidItem(m_item);

    /// <summary>
    /// Sets a key value for the stored item.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <param name="key">Key handle.</param>
    /// <param name="value">Key value.</param>
    /// <returns>The same entry for chaining.</returns>
    public PoolEntry<T> Set<TKey>(PoolKey<TKey> key, TKey value) where TKey : notnull
    {
        m_pool.SetKey(m_item, key, value);
        return this;
    }
}
