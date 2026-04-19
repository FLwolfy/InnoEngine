using System;

namespace Inno.Core.Job;

/// <summary>
/// Static facade for job scheduling APIs backed by <see cref="JobSystemManager"/>.
/// </summary>
public static class JobSystem
{
    /// <summary>
    /// Gets worker-thread count of the active scheduler.
    /// </summary>
    public static int workerCount => JobSystemManager.current.workerCount;

    /// <summary>
    /// Schedules a job with no dependencies.
    /// </summary>
    public static JobHandle Schedule(Action job)
    {
        return JobSystemManager.current.Schedule(job);
    }

    /// <summary>
    /// Schedules a job with optional state and dependencies.
    /// </summary>
    public static JobHandle Schedule(Action<object?> job, object? state,
        ReadOnlySpan<JobHandle> dependencies)
    {
        return JobSystemManager.current.Schedule(job, state, dependencies);
    }

    /// <summary>
    /// Creates a synchronization handle that completes after all provided dependencies complete.
    /// </summary>
    public static JobHandle CombineDependencies(ReadOnlySpan<JobHandle> dependencies)
    {
        return JobSystemManager.current.CombineDependencies(dependencies);
    }

    /// <summary>
    /// Schedules a parallel-for range split into batches.
    /// </summary>
    public static JobHandle ParallelFor(int length, int batchSize, Action<int, int> body)
    {
        return JobSystemManager.current.ParallelFor(length, batchSize, body);
    }

    /// <summary>
    /// Blocks until a handle is complete.
    /// </summary>
    public static void Complete(JobHandle handle)
    {
        JobSystemManager.current.Complete(handle);
    }

    /// <summary>
    /// Blocks until all handles are complete.
    /// </summary>
    public static void CompleteAll(ReadOnlySpan<JobHandle> handles)
    {
        JobSystemManager.current.CompleteAll(handles);
    }

    /// <summary>
    /// Enqueues a callback that must execute on the main thread.
    /// </summary>
    public static void EnqueueMainThread(Action action)
    {
        JobSystemManager.current.EnqueueMainThread(action);
    }
}