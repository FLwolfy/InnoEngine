using System;

namespace Inno.Core.JobSystem;

/// <summary>
/// Job scheduler and worker pool interface.
/// </summary>
public interface IJobSystem : IDisposable
{
    /// <summary>
    /// Gets the number of worker threads.
    /// </summary>
    int workerCount { get; }

    /// <summary>
    /// Begins a frame scheduling scope.
    /// </summary>
    void BeginFrame();

    /// <summary>
    /// Ends the current frame scheduling scope, ensuring all frame jobs are completed.
    /// </summary>
    void EndFrame();

    /// <summary>
    /// Schedules a job with no dependencies.
    /// </summary>
    JobHandle Schedule(Action job);

    /// <summary>
    /// Schedules a job with optional state and dependencies.
    /// </summary>
    JobHandle Schedule(Action<object?> job, object? state, ReadOnlySpan<JobHandle> dependencies);

    /// <summary>
    /// Creates a synchronization handle that completes after all provided dependencies complete.
    /// </summary>
    JobHandle CombineDependencies(ReadOnlySpan<JobHandle> dependencies);

    /// <summary>
    /// Schedules a parallel-for range split into batches.
    /// </summary>
    JobHandle ParallelFor(int length, int batchSize, Action<int, int> body);

    /// <summary>
    /// Blocks until the given handle is completed.
    /// </summary>
    void Complete(JobHandle handle);

    /// <summary>
    /// Blocks until every provided handle is completed.
    /// </summary>
    void CompleteAll(ReadOnlySpan<JobHandle> handles);

    /// <summary>
    /// Enqueues a callback that must execute on the main thread.
    /// </summary>
    void EnqueueMainThread(Action action);

    /// <summary>
    /// Executes queued main-thread callbacks on the main thread.
    /// </summary>
    void DrainMainThreadQueue();
}
