using System.Collections.Generic;

namespace Inno.Core.Storage;

internal sealed class NullKeyOrdering<TKey> : IKeyOrdering<TKey> where TKey : notnull
{
    /// <summary>
    /// Adds one key to the indexed membership set.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    public void AddKey(TKey key) { }
    /// <summary>
    /// Removes one key from the indexed membership set.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    public void RemoveKey(TKey key) { }
    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    public void Clear() { }
    /// <summary>
    /// Enumerates a stable view of the currently indexed keys.
    /// </summary>
    /// <returns>
    /// The validated ienumerabletkey that represents the completed operation.
    /// </returns>
    public IEnumerable<TKey> Enumerate() { yield break; }
}
