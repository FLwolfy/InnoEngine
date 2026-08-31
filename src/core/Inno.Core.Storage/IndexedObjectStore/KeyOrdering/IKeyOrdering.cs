using System.Collections.Generic;

namespace Inno.Core.Storage;

internal interface IKeyOrdering<TKey> where TKey : notnull
{
    void AddKey(TKey key);
    void RemoveKey(TKey key);
    void Clear();
    IEnumerable<TKey> Enumerate();
}
