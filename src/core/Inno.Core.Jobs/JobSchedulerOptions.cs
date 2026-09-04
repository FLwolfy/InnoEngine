using System;

namespace Inno.Core.Jobs;

/// <summary>
/// Defines worker-pool options for one isolated <see cref="JobScheduler"/>.
/// </summary>
public readonly struct JobSchedulerOptions
{
    private const int DEFAULT_MAX_WORKER_THREADS = 64;

    /// <summary>
    /// Number of worker threads.
    /// Set to 0 to use <c>max(1, Environment.ProcessorCount - 1)</c>.
    /// </summary>
    public int workerCount { get; init; }

    internal int ResolveWorkerCount()
    {
        if (workerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), "workerCount cannot be negative.");
        }

        if (workerCount == 0)
        {
            var cpuCount = Environment.ProcessorCount;
            var resolved = cpuCount > 1 ? cpuCount - 1 : 1;
            return Math.Clamp(resolved, 1, DEFAULT_MAX_WORKER_THREADS);
        }

        return Math.Clamp(workerCount, 1, DEFAULT_MAX_WORKER_THREADS);
    }
}
