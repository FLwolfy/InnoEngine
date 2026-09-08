using System;

namespace Inno.Extensibility.Reload;

/// <summary>
/// Configures forced-collection cadence and the retention failure threshold.
/// </summary>
public sealed class AssemblyUnloadBarrierOptions
{
    /// <summary>
    /// Gets the minimum interval between full collection attempts.
    /// </summary>
    public TimeSpan collectionInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets the duration after which a still-reachable generation faults the reload subsystem.
    /// </summary>
    public TimeSpan retentionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
