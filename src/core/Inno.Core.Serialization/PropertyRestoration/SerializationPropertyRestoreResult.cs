using System.Collections.Generic;

namespace Inno.Core.Serialization;

/// <summary>
/// Summarizes an independently captured property restore operation.
/// </summary>
public sealed class SerializationPropertyRestoreResult
{
    internal SerializationPropertyRestoreResult(
        int restoredCount,
        int ignoredCount,
        IReadOnlyList<SerializationPropertyRestoreFailure> failures)
    {
        this.restoredCount = restoredCount;
        this.ignoredCount = ignoredCount;
        this.failures = failures;
    }

    /// <summary>
    /// Gets the number of properties successfully restored.
    /// </summary>
    public int restoredCount { get; }

    /// <summary>
    /// Gets the number of removed or non-deserializable properties ignored by schema matching.
    /// </summary>
    public int ignoredCount { get; }

    /// <summary>
    /// Gets properties skipped because their previous data was incompatible.
    /// </summary>
    public IReadOnlyList<SerializationPropertyRestoreFailure> failures { get; }

    /// <summary>
    /// Gets whether every matching property was restored successfully.
    /// </summary>
    public bool success => failures.Count == 0;
}
