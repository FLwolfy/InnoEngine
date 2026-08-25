namespace Inno.Core.Storage;

/// <summary>
/// Runtime handle used for O(1) dense/sparse lookup with stale-reference protection.
/// </summary>
internal readonly record struct IndexedObjectRuntimeHandle(int slot, uint generation);
