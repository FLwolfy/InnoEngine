using System.Collections.Generic;

namespace Inno.Core.Storage;

internal interface IKeyStorage<TKey, T> where T : class where TKey : notnull
{
    bool Add(TKey key, T item);
    void Remove(TKey key, T item);
    bool TryGetSingle(TKey key, out T? item);
    int GetCount(TKey key);
    bool Contains(TKey key, T item);
    bool IsKeyEmpty(TKey key);
    HashSet<T>? GetSet(TKey key);
    void Clear();
}