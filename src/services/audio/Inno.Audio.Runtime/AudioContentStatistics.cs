using System;

namespace Inno.Audio.Runtime;

/// <summary>
/// Reports immutable content admission pressure from the most recent control-thread collection.
/// </summary>
public readonly record struct AudioContentStatistics
{
    /// <summary>
    /// Creates a content admission snapshot without retaining providers or host content.
    /// </summary>
    /// <param name="capacity">
    /// Combined emitter and listener budget available to the update.
    /// </param>
    /// <param name="acceptedSnapshots">
    /// Snapshots accepted from fully validated provider contributions.
    /// </param>
    /// <param name="rejectedProviders">
    /// Provider contributions discarded because submission, validation, or capacity admission failed.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count is negative or accepted snapshots exceed capacity.
    /// </exception>
    public AudioContentStatistics(int capacity, int acceptedSnapshots, int rejectedProviders)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(acceptedSnapshots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(acceptedSnapshots, capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(rejectedProviders);
        this.capacity = capacity;
        this.acceptedSnapshots = acceptedSnapshots;
        this.rejectedProviders = rejectedProviders;
    }

    /// <summary>
    /// Gets the immutable combined snapshot budget assigned to the runtime.
    /// </summary>
    public int capacity { get; }

    /// <summary>
    /// Gets accepted snapshots, bounded by capacity and excluding every rejected partial contribution.
    /// </summary>
    public int acceptedSnapshots { get; }

    /// <summary>
    /// Gets rejected contributions in this collection, including overflow and duplicate identities.
    /// </summary>
    public int rejectedProviders { get; }
}
