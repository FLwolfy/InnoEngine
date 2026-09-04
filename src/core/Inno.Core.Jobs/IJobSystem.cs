using System;

namespace Inno.Core.Jobs;

/// <summary>
/// Job scheduler and worker pool interface.
/// </summary>
internal interface IJobSystem : IDisposable
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
    /// <param name="job">
    /// The callback executed once by the scheduler.
    /// </param>
    /// <returns>
    /// A generation-scoped handle that represents the scheduled completion.
    /// </returns>
    JobHandle Schedule(Action job);

    /// <summary>
    /// Schedules a job with optional state and dependencies.
    /// </summary>
    /// <param name="job">
    /// The callback that receives the supplied state after every dependency completes.
    /// </param>
    /// <param name="state">
    /// The caller-owned state passed to the job callback.
    /// </param>
    /// <param name="dependencies">
    /// The job handles that must complete before this callback can execute.
    /// </param>
    /// <returns>
    /// A generation-scoped handle that represents the scheduled completion.
    /// </returns>
    JobHandle Schedule(Action<object?> job, object? state, ReadOnlySpan<JobHandle> dependencies);

    /// <summary>
    /// Creates a synchronization handle that completes after all provided dependencies complete.
    /// </summary>
    /// <param name="dependencies">
    /// The handles whose completion is combined without rescheduling their work.
    /// </param>
    /// <returns>
    /// A handle that completes only after every supplied dependency.
    /// </returns>
    JobHandle CombineDependencies(ReadOnlySpan<JobHandle> dependencies);

    /// <summary>
    /// Schedules a parallel-for range split into batches.
    /// </summary>
    /// <param name="length">
    /// The non-negative number of indexed items to process.
    /// </param>
    /// <param name="batchSize">
    /// The positive maximum number of contiguous items assigned to one callback.
    /// </param>
    /// <param name="body">
    /// The callback that receives each batch start and exclusive end index.
    /// </param>
    /// <returns>
    /// A handle that completes after every scheduled batch.
    /// </returns>
    JobHandle ParallelFor(int length, int batchSize, Action<int, int> body);

    /// <summary>
    /// Blocks until the given handle is completed.
    /// </summary>
    /// <param name="handle">
    /// The generation-scoped job handle to complete.
    /// </param>
    void Complete(JobHandle handle);

    /// <summary>
    /// Blocks until every provided handle is completed.
    /// </summary>
    /// <param name="handles">
    /// The generation-scoped job handles that must all complete.
    /// </param>
    void CompleteAll(ReadOnlySpan<JobHandle> handles);

    /// <summary>
    /// Enqueues a callback that must execute on the main thread.
    /// </summary>
    /// <param name="action">
    /// The callback transferred to the scheduler's ordered main-thread queue.
    /// </param>
    void EnqueueMainThread(Action action);

    /// <summary>
    /// Executes queued main-thread callbacks on the main thread.
    /// </summary>
    void DrainMainThreadQueue();
}
