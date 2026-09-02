using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Inno.Core.Jobs.Internal;

namespace Inno.Core.Jobs;

/// <summary>
/// Single-threaded deterministic job scheduler.
/// </summary>
internal sealed class SingleThreadJobSystem : IJobSystem
{
    private static readonly Action<object?> s_noop = static _ => { };
    private static readonly Action<object?> s_actionInvoker = static state => ((Action?)state)?.Invoke();
    private static readonly Action<object?> s_parallelRangeInvoker = static state =>
    {
        var rangeState = (ParallelForRangeState?)state
            ?? throw new InvalidOperationException("Parallel-for state cannot be null.");
        rangeState.body(rangeState.startInclusive, rangeState.endExclusive);
    };

    private readonly object m_jobsGate = new();
    private readonly ConcurrentQueue<Action> m_mainThreadQueue = new();
    private readonly Queue<int> m_readyQueue = new();
    private readonly List<JobRecord> m_jobs = [];
    private readonly Stack<int> m_freeIndices = [];
    private readonly List<int> m_frameJobs = [];

    private readonly int m_mainThreadId;
    private bool m_frameActive;
    private bool m_disposed;

    /// <summary>
    /// Creates a deterministic single-thread scheduler.
    /// </summary>
    internal SingleThreadJobSystem()
    {
        m_mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public int workerCount => 0;

    /// <summary>
    /// Begins a frame-scoped operation and makes queued work visible.
    /// </summary>
    public void BeginFrame()
    {
        ThrowIfDisposed();
        EnsureMainThread();
        lock (m_jobsGate)
        {
            if (m_frameActive)
            {
                throw new InvalidOperationException("BeginFrame was called while a frame is already active.");
            }

            m_frameActive = true;
            m_frameJobs.Clear();
            m_readyQueue.Clear();
        }
    }

    /// <summary>
    /// Commits the current frame-scoped operation and returns its completion identity.
    /// </summary>
    public void EndFrame()
    {
        ThrowIfDisposed();
        EnsureMainThread();

        List<JobHandle> handles = [];
        lock (m_jobsGate)
        {
            if (!m_frameActive)
            {
                throw new InvalidOperationException("EndFrame was called without an active frame.");
            }

            handles.Capacity = m_frameJobs.Count;
            for (var i = 0; i < m_frameJobs.Count; i++)
            {
                var index = m_frameJobs[i];
                if (!TryGetActiveRecordNoLock(index, out var record))
                {
                    continue;
                }

                handles.Add(new JobHandle(index, record.version));
            }
        }

        List<Exception>? faults = null;
        for (var i = 0; i < handles.Count; i++)
        {
            try
            {
                Complete(handles[i]);
            }
            catch (Exception ex)
            {
                faults ??= [];
                faults.Add(ex);
            }
        }

        lock (m_jobsGate)
        {
            for (var i = 0; i < m_frameJobs.Count; i++)
            {
                var index = m_frameJobs[i];
                if (!TryGetActiveRecordNoLock(index, out var record))
                {
                    continue;
                }

                if (record.executionState != JobExecutionState.Completed)
                {
                    continue;
                }

                record.ResetForReuse();
                m_freeIndices.Push(index);
            }

            m_frameJobs.Clear();
            m_frameActive = false;
        }

        if (faults is { Count: > 0 })
        {
            throw new AggregateException("One or more jobs failed during EndFrame.", faults);
        }
    }

    /// <summary>
    /// Queues the requested work for execution by the owning scheduler.
    /// </summary>
    /// <param name="job">
    /// The callback invoked by schedule within the operation's owned lifetime.
    /// </param>
    /// <returns>
    /// The validated job handle that represents the completed operation.
    /// </returns>
    public JobHandle Schedule(Action job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Schedule(s_actionInvoker, job, ReadOnlySpan<JobHandle>.Empty);
    }

    /// <summary>
    /// Queues the requested work for execution by the owning scheduler.
    /// </summary>
    /// <param name="job">
    /// The callback invoked by schedule within the operation's owned lifetime.
    /// </param>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    /// <param name="dependencies">
    /// The dependencies consumed by schedule; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated job handle that represents the completed operation.
    /// </returns>
    public JobHandle Schedule(Action<object?> job, object? state, ReadOnlySpan<JobHandle> dependencies)
    {
        ArgumentNullException.ThrowIfNull(job);
        ThrowIfDisposed();
        EnsureMainThread();

        List<int>? readyIndices = null;
        JobHandle handle;
        lock (m_jobsGate)
        {
            EnsureFrameActiveNoLock();

            var index = AllocateJobIndexNoLock(job, state);
            var record = m_jobs[index];

            var dependencyCount = 0;
            for (var i = 0; i < dependencies.Length; i++)
            {
                var dependency = dependencies[i];
                if (!dependency.isValid)
                {
                    throw new ArgumentException("Dependency handle is invalid.", nameof(dependencies));
                }

                var dependencyRecord = ResolveHandleNoLock(dependency);
                if (dependencyRecord.executionState == JobExecutionState.Completed)
                {
                    continue;
                }

                dependencyRecord.dependents ??= [];
                dependencyRecord.dependents.Add(index);
                dependencyCount++;
            }

            record.remainingDependencies = dependencyCount;
            if (dependencyCount == 0)
            {
                record.executionState = JobExecutionState.Queued;
                readyIndices = [index];
            }

            handle = new JobHandle(index, record.version);
        }

        if (readyIndices is { Count: > 0 })
        {
            QueueReadyJobs(readyIndices);
        }

        return handle;
    }

    /// <summary>
    /// Combines the supplied job dependencies into one completion handle.
    /// </summary>
    /// <param name="dependencies">
    /// The dependencies consumed by combine dependencies; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated job handle that represents the completed operation.
    /// </returns>
    public JobHandle CombineDependencies(ReadOnlySpan<JobHandle> dependencies)
    {
        return Schedule(s_noop, null, dependencies);
    }

    /// <summary>
    /// Schedules the indexed range in batches and returns its completion handle.
    /// </summary>
    /// <param name="length">
    /// The length consumed by parallel for; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="batchSize">
    /// The batch size consumed by parallel for; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="body">
    /// The callback invoked by parallel for within the operation's owned lifetime.
    /// </param>
    /// <returns>
    /// The validated job handle that represents the completed operation.
    /// </returns>
    public JobHandle ParallelFor(int length, int batchSize, Action<int, int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "length cannot be negative.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "batchSize must be greater than zero.");
        }

        if (length == 0)
        {
            return Schedule(s_noop, null, ReadOnlySpan<JobHandle>.Empty);
        }

        var chunkCount = (length + batchSize - 1) / batchSize;
        var handles = new JobHandle[chunkCount];
        var chunkIndex = 0;
        for (var start = 0; start < length; start += batchSize)
        {
            var end = Math.Min(start + batchSize, length);
            var state = new ParallelForRangeState(body, start, end);
            handles[chunkIndex++] = Schedule(s_parallelRangeInvoker, state, ReadOnlySpan<JobHandle>.Empty);
        }

        return CombineDependencies(handles);
    }

    /// <summary>
    /// Completes the committed operation and releases temporary state.
    /// </summary>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    public void Complete(JobHandle handle)
    {
        if (!handle.isValid)
        {
            throw new ArgumentException("Handle is invalid.", nameof(handle));
        }

        ThrowIfDisposed();
        EnsureMainThread();

        while (true)
        {
            if (TryGetCompletion(handle, out var exception))
            {
                if (exception is not null)
                {
                    throw new InvalidOperationException("Job execution failed.", exception);
                }

                return;
            }

            if (!TryExecuteSingleQueuedJob())
            {
                throw new InvalidOperationException("No executable jobs are available while waiting for completion.");
            }
        }
    }

    /// <summary>
    /// Waits until every supplied job handle has completed.
    /// </summary>
    /// <param name="handles">
    /// The handles consumed by complete all; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void CompleteAll(ReadOnlySpan<JobHandle> handles)
    {
        for (var i = 0; i < handles.Length; i++)
        {
            Complete(handles[i]);
        }
    }

    /// <summary>
    /// Queues work for ordered execution on the owning main thread.
    /// </summary>
    /// <param name="action">
    /// The caller-provided delegate invoked within this operation's lifetime.
    /// </param>
    public void EnqueueMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        m_mainThreadQueue.Enqueue(action);
    }

    /// <summary>
    /// Executes every queued main-thread callback in submission order.
    /// </summary>
    public void DrainMainThreadQueue()
    {
        ThrowIfDisposed();
        EnsureMainThread();

        List<Exception>? exceptions = null;
        while (m_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more main-thread callbacks failed.", exceptions);
        }
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        m_disposed = true;
    }

    private bool TryExecuteSingleQueuedJob()
    {
        int index;
        lock (m_jobsGate)
        {
            if (m_readyQueue.Count == 0)
            {
                return false;
            }

            index = m_readyQueue.Dequeue();
            if (!TryGetActiveRecordNoLock(index, out var record) || record.executionState != JobExecutionState.Queued)
            {
                return false;
            }

            record.executionState = JobExecutionState.Running;
        }

        Action<object?>? callback;
        object? state;
        lock (m_jobsGate)
        {
            var record = m_jobs[index];
            callback = record.callback;
            state = record.state;
        }

        Exception? fault = null;
        try
        {
            callback?.Invoke(state);
        }
        catch (Exception ex)
        {
            fault = ex;
        }

        List<int>? readyIndices = null;
        lock (m_jobsGate)
        {
            if (!TryGetActiveRecordNoLock(index, out var record))
            {
                return true;
            }

            record.executionState = JobExecutionState.Completed;
            record.exception = fault;
            record.callback = null;
            record.state = null;

            if (record.dependents is { Count: > 0 })
            {
                readyIndices = [];
                for (var i = 0; i < record.dependents.Count; i++)
                {
                    var dependentIndex = record.dependents[i];
                    if (!TryGetActiveRecordNoLock(dependentIndex, out var dependent))
                    {
                        continue;
                    }

                    dependent.remainingDependencies--;
                    if (dependent.remainingDependencies == 0 && dependent.executionState == JobExecutionState.Created)
                    {
                        dependent.executionState = JobExecutionState.Queued;
                        readyIndices.Add(dependentIndex);
                    }
                }

                record.dependents.Clear();
            }
        }

        if (readyIndices is { Count: > 0 })
        {
            QueueReadyJobs(readyIndices);
        }

        return true;
    }

    private void QueueReadyJobs(List<int> readyIndices)
    {
        lock (m_jobsGate)
        {
            for (var i = 0; i < readyIndices.Count; i++)
            {
                m_readyQueue.Enqueue(readyIndices[i]);
            }
        }
    }

    private int AllocateJobIndexNoLock(Action<object?> callback, object? state)
    {
        int index;
        if (m_freeIndices.Count > 0)
        {
            index = m_freeIndices.Pop();
        }
        else
        {
            index = m_jobs.Count;
            m_jobs.Add(new JobRecord());
        }

        var record = m_jobs[index];
        record.ResetForAllocation(callback, state);
        m_frameJobs.Add(index);
        return index;
    }

    private bool TryGetCompletion(JobHandle handle, out Exception? exception)
    {
        lock (m_jobsGate)
        {
            var record = ResolveHandleNoLock(handle);
            if (record.executionState != JobExecutionState.Completed)
            {
                exception = null;
                return false;
            }

            exception = record.exception;
            return true;
        }
    }

    private JobRecord ResolveHandleNoLock(JobHandle handle)
    {
        if (handle.index < 0 || handle.index >= m_jobs.Count)
        {
            throw new InvalidOperationException("Job handle index is out of range.");
        }

        var record = m_jobs[handle.index];
        if (!record.inUse || record.version != handle.version)
        {
            throw new InvalidOperationException("Job handle is stale or no longer valid.");
        }

        return record;
    }

    private bool TryGetActiveRecordNoLock(int index, out JobRecord record)
    {
        if (index < 0 || index >= m_jobs.Count)
        {
            record = null!;
            return false;
        }

        record = m_jobs[index];
        return record.inUse;
    }

    private void EnsureFrameActiveNoLock()
    {
        if (!m_frameActive)
        {
            throw new InvalidOperationException("Schedule requires an active frame. Call BeginFrame() first.");
        }
    }

    private void EnsureMainThread()
    {
        if (Environment.CurrentManagedThreadId != m_mainThreadId)
        {
            throw new InvalidOperationException("This operation must be executed on the main thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(SingleThreadJobSystem));
        }
    }

    private sealed class ParallelForRangeState
    {
        internal readonly Action<int, int> body;
        internal readonly int startInclusive;
        internal readonly int endExclusive;

        internal ParallelForRangeState(Action<int, int> body, int startInclusive, int endExclusive)
        {
            this.body = body;
            this.startInclusive = startInclusive;
            this.endExclusive = endExclusive;
        }
    }
}
