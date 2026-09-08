using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Extensibility.Reload;

/// <summary>
/// Reports retired collectible generations that remained reachable after the unload barrier threshold.
/// </summary>
public sealed class AssemblyUnloadException : InvalidOperationException
{
    /// <summary>
    /// Creates a terminal unload failure with stable generation diagnostics.
    /// </summary>
    /// <param name="retainedGenerations">
    /// Descriptions of every retired generation that remains reachable.
    /// </param>
    /// <param name="elapsed">
    /// The elapsed barrier duration.
    /// </param>
    /// <param name="collectionAttempts">
    /// The number of full collection cycles already performed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="retainedGenerations"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when no retained generation is supplied.
    /// </exception>
    public AssemblyUnloadException(
        IReadOnlyList<string> retainedGenerations,
        TimeSpan elapsed,
        int collectionAttempts)
        : base(CreateMessage(retainedGenerations, elapsed, collectionAttempts))
    {
        ArgumentNullException.ThrowIfNull(retainedGenerations);
        if (retainedGenerations.Count == 0)
            throw new ArgumentException("An unload failure requires at least one retained generation.", nameof(retainedGenerations));
        this.retainedGenerations = retainedGenerations.ToArray();
        this.elapsed = elapsed;
        this.collectionAttempts = collectionAttempts;
    }

    /// <summary>
    /// Gets stable descriptions of every generation that prevented completion.
    /// </summary>
    public IReadOnlyList<string> retainedGenerations { get; }

    /// <summary>
    /// Gets the elapsed duration before the barrier faulted.
    /// </summary>
    public TimeSpan elapsed { get; }

    /// <summary>
    /// Gets the number of full collection cycles performed before failure.
    /// </summary>
    public int collectionAttempts { get; }

    private static string CreateMessage(
        IReadOnlyList<string> retainedGenerations,
        TimeSpan elapsed,
        int collectionAttempts)
    {
        ArgumentNullException.ThrowIfNull(retainedGenerations);
        if (retainedGenerations.Count == 0)
            throw new ArgumentException("An unload failure requires at least one retained generation.", nameof(retainedGenerations));
        return
            $"Retired assembly generations remained reachable for {elapsed.TotalSeconds:F1} seconds across " +
            $"{collectionAttempts} full garbage-collection attempts: {string.Join(", ", retainedGenerations)}. " +
            "A Type, object, delegate, extension, task, subscription, thread, native callback, or other generation-bound " +
            "reference is still retained. The reload subsystem is faulted and requires a host restart.";
    }
}
