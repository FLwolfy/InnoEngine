using System.Collections.Generic;

namespace Inno.Core.Storage;

internal sealed class SortedKeyOrdering<TKey> : IKeyOrdering<TKey> where TKey : notnull
{
    private readonly SortedSet<TKey> m_keys;

    public SortedKeyOrdering(IComparer<TKey> comparer)
    {
        m_keys = new SortedSet<TKey>(comparer);
    }

    public void AddKey(TKey key) => m_keys.Add(key);

    public void RemoveKey(TKey key) => m_keys.Remove(key);

    public void Clear() => m_keys.Clear();

    public IEnumerable<TKey> Enumerate()
    {
        foreach (var key in m_keys)
            yield return key;
    }
}
