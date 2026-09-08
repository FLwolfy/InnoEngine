namespace Inno.Core.Collections;

/// <summary>
/// Runtime handle used for O(1) dense/sparse lookup with stale-reference protection.
/// </summary>
/// <param name="slot">
/// The slot used to initialize this instance.
/// </param>
/// <param name="generation">
/// The generation used to initialize this instance.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
internal readonly record struct IndexedObjectRuntimeHandle(int slot, uint generation);
