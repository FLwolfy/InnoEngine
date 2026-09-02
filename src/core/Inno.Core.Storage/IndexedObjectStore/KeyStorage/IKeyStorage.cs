using System.Collections.Generic;

namespace Inno.Core.Storage;

internal interface IKeyStorage<TKey, T> where T : class where TKey : notnull
{
    /// <summary>
    /// Adds the supplied value while preserving the collection's invariants.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool Add(TKey key, T item);
    /// <summary>
    /// Removes the requested value while preserving the collection's invariants.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    void Remove(TKey key, T item);
    /// <summary>
    /// Attempts to get single without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool TryGetSingle(TKey key, out T? item);
    /// <summary>
    /// Retrieves the requested count value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    int GetCount(TKey key);
    /// <summary>
    /// Determines whether current state contains the requested value value.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <param name="item">
    /// The stored item associated with the validated handle.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool Contains(TKey key, T item);
    /// <summary>
    /// Determines whether a key currently has no indexed values.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    bool IsKeyEmpty(TKey key);
    /// <summary>
    /// Retrieves the requested set value from current authoritative state.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key associated with this event.
    /// </param>
    /// <returns>
    /// The validated hash sett? that represents the completed operation.
    /// </returns>
    HashSet<T>? GetSet(TKey key);
    /// <summary>
    /// Removes all retained entries and returns the instance to an empty reusable state.
    /// </summary>
    void Clear();
}
