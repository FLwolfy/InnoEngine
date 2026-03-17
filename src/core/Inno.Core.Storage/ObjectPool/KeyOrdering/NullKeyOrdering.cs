using System.Collections.Generic;

namespace Inno.Core.Storage;

internal sealed class NullKeyOrdering<TKey> : IKeyOrdering<TKey> where TKey : notnull
{
    public void AddKey(TKey key) { }
    public void RemoveKey(TKey key) { }
    public void Clear() { }
    public IEnumerable<TKey> Enumerate() { yield break; }
}