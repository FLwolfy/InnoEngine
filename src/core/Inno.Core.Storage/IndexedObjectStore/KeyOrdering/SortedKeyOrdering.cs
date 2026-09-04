using System.Collections.Generic;

namespace Inno.Core.Storage;

internal sealed class SortedKeyOrdering<TKey> : IKeyOrdering<TKey> where TKey : notnull
{
    private readonly SortedSet<TKey> m_keys;

    /// <summary>
    /// Creates a validated sorted key ordering instance.
    /// </summary>
    /// <param name="comparer">
    /// The comparer consumed by sorted key ordering; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public SortedKeyOrdering(IComparer<TKey> comparer)
    {
        m_keys = new SortedSet<TKey>(comparer);
    }

    /// <summary>
    /// Adds one key to the indexed membership set.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    public void AddKey(TKey key) => m_keys.Add(key);

    /// <summary>
    /// Removes one key from the indexed membership set.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    public void RemoveKey(TKey key) => m_keys.Remove(key);

    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    public void Clear() => m_keys.Clear();

    /// <summary>
    /// Enumerates a stable view of the currently indexed keys.
    /// </summary>
    /// <returns>
    /// The validated ienumerabletkey that represents the completed operation.
    /// </returns>
    public IEnumerable<TKey> Enumerate()
    {
        foreach (var key in m_keys)
            yield return key;
    }
}
