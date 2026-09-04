using System;

namespace Inno.Core.Jobs;

/// <summary>
/// Owns one isolated frame-scoped job scheduler and hides its concrete execution strategy.
/// </summary>
public sealed class JobScheduler : IDisposable
{
    private readonly IJobSystem m_implementation;

    /// <summary>
    /// Creates a scheduler using the requested execution strategy.
    /// </summary>
    /// <param name="mode">
    /// The execution strategy owned by the scheduler.
    /// </param>
    /// <param name="options">
    /// Worker-pool settings used when <paramref name="mode"/> is <see cref="JobExecutionMode.WorkerPool"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="mode"/> is not a defined execution strategy.
    /// </exception>
    public JobScheduler(JobExecutionMode mode, JobSchedulerOptions options = default)
    {
        m_implementation = mode switch
        {
            JobExecutionMode.SingleThread => new SingleThreadJobSystem(),
            JobExecutionMode.WorkerPool => new WorkStealingJobSystem(options),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown job execution mode.")
        };
    }

    /// <summary>
    /// Gets the number of background workers owned by this scheduler.
    /// </summary>
    public int workerCount => m_implementation.workerCount;

    /// <summary>
    /// Opens a frame scheduling scope on the owner thread.
    /// </summary>
    public void BeginFrame() => m_implementation.BeginFrame();

    /// <summary>
    /// Completes every frame job and closes the current frame scheduling scope.
    /// </summary>
    public void EndFrame() => m_implementation.EndFrame();

    /// <summary>
    /// Schedules one parameterless job in the current frame.
    /// </summary>
    /// <param name="job">
    /// The operation to execute once its implicit empty dependency set is ready.
    /// </param>
    /// <returns>
    /// A generation-safe handle representing the scheduled operation.
    /// </returns>
    public JobHandle Schedule(Action job) => m_implementation.Schedule(job);

    /// <summary>
    /// Schedules one stateful job after all supplied dependencies complete.
    /// </summary>
    /// <param name="job">
    /// The operation receiving <paramref name="state"/>.
    /// </param>
    /// <param name="state">
    /// Optional caller-owned state passed to the operation.
    /// </param>
    /// <param name="dependencies">
    /// Handles that must complete before the operation becomes runnable.
    /// </param>
    /// <returns>
    /// A generation-safe handle representing the scheduled operation.
    /// </returns>
    public JobHandle Schedule(
        Action<object?> job,
        object? state,
        ReadOnlySpan<JobHandle> dependencies)
        => m_implementation.Schedule(job, state, dependencies);

    /// <summary>
    /// Creates one handle that completes after every supplied dependency.
    /// </summary>
    /// <param name="dependencies">
    /// Handles combined by the returned synchronization point.
    /// </param>
    /// <returns>
    /// A generation-safe combined handle.
    /// </returns>
    public JobHandle CombineDependencies(ReadOnlySpan<JobHandle> dependencies)
        => m_implementation.CombineDependencies(dependencies);

    /// <summary>
    /// Schedules a range as independent contiguous batches.
    /// </summary>
    /// <param name="length">
    /// The exclusive upper bound of the complete range.
    /// </param>
    /// <param name="batchSize">
    /// The maximum number of indices in one batch.
    /// </param>
    /// <param name="body">
    /// The callback receiving each batch's inclusive start and exclusive end.
    /// </param>
    /// <returns>
    /// A handle that completes after every batch.
    /// </returns>
    public JobHandle ParallelFor(int length, int batchSize, Action<int, int> body)
        => m_implementation.ParallelFor(length, batchSize, body);

    /// <summary>
    /// Blocks the calling thread until one scheduled operation completes.
    /// </summary>
    /// <param name="handle">
    /// The generation-safe operation handle to complete.
    /// </param>
    public void Complete(JobHandle handle) => m_implementation.Complete(handle);

    /// <summary>
    /// Blocks the calling thread until every supplied operation completes.
    /// </summary>
    /// <param name="handles">
    /// The generation-safe operation handles to complete.
    /// </param>
    public void CompleteAll(ReadOnlySpan<JobHandle> handles)
        => m_implementation.CompleteAll(handles);

    /// <summary>
    /// Enqueues an operation that must run on the scheduler owner thread.
    /// </summary>
    /// <param name="action">
    /// The owner-thread operation.
    /// </param>
    public void EnqueueMainThread(Action action)
        => m_implementation.EnqueueMainThread(action);

    /// <summary>
    /// Executes all owner-thread operations queued before and during this drain.
    /// </summary>
    public void DrainMainThreadQueue()
        => m_implementation.DrainMainThreadQueue();

    /// <summary>
    /// Stops worker threads and releases every pending scheduling resource.
    /// </summary>
    public void Dispose() => m_implementation.Dispose();
}
