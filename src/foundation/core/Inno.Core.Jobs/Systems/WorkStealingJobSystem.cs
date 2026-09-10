using System;
using Inno.Core.Execution;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Inno.Core.Jobs.Internal;

namespace Inno.Core.Jobs;

/// <summary>
/// Multi-threaded job scheduler backed by a fixed worker pool and work-stealing queues.
/// </summary>
internal sealed class WorkStealingJobSystem : IJobSystem
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
    private readonly ConcurrentQueue<int> m_globalInjectQueue = new();
    private readonly MainThreadWorkQueue m_mainThreadQueue;
    private readonly int m_maxFrameJobs;
    private int m_peakFrameJobs;
    private long m_rejectedJobs;
    private readonly List<Exception> m_shutdownFailures = [];
    private readonly ManualResetEventSlim m_completionSignal = new(false);
    private readonly SemaphoreSlim m_wakeSignal = new(0);
    private readonly ThreadLocal<int> m_currentWorkerId = new(() => -1, trackAllValues: false);
    private readonly WorkerRuntime[] m_workers;

    private readonly List<JobRecord> m_jobs = [];
    private readonly Stack<int> m_freeIndices = [];
    private readonly List<int> m_frameJobs = [];

    private readonly int m_mainThreadId;
    private readonly int m_workerCount;
    private bool m_disposed;
    private bool m_frameActive;
    private int m_running;

    /// <summary>
    /// Creates a job system with default options.
    /// </summary>
    internal WorkStealingJobSystem()
        : this(new JobSchedulerOptions())
    {
    }

    /// <summary>
    /// Creates a job system with explicit options.
    /// </summary>
    internal WorkStealingJobSystem(JobSchedulerOptions options)
    {
        m_mainThreadQueue = options.CreateMainThreadQueue();
        m_maxFrameJobs = options.ResolveFrameCapacity();
        m_mainThreadId = Environment.CurrentManagedThreadId;
        m_workerCount = options.ResolveWorkerCount();
        m_workers = new WorkerRuntime[m_workerCount];
        m_running = 1;

        int started = 0;
        try
        {
            for (var i = 0; i < m_workers.Length; i++)
            {
                var workerId = i;
                m_workers[i] = new WorkerRuntime(workerId, () => WorkerLoop(workerId));
            }
            for (; started < m_workers.Length; started++)
                m_workers[started].thread.Start();
        }
        catch
        {
            Interlocked.Exchange(ref m_running, 0);
            for (int index = 0; index < started; index++) m_wakeSignal.Release();
            // No work can have been admitted before construction returns.
            for (int index = 0; index < started; index++) m_workers[index].thread.Join();
            m_currentWorkerId.Dispose();
            m_wakeSignal.Dispose();
            m_completionSignal.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the scalar measurement or identity associated with the current state.
    /// </summary>
    public JobSchedulerStatistics statistics
    {
        get
        {
            lock (m_jobsGate) return m_mainThreadQueue.statistics with
            {
                frameJobs = m_frameJobs.Count, peakFrameJobs = m_peakFrameJobs, rejectedJobs = m_rejectedJobs
            };
        }
    }

    /// <summary>
    /// Gets the worker count scalar measured or assigned by the current instance.
    /// </summary>
    public int workerCount => m_workerCount;

    /// <summary>
    /// Begins a frame-scoped operation and makes queued work visible.
    /// </summary>
    public void BeginFrame()
    {
        ThrowIfDisposed();
        lock (m_jobsGate)
        {
            if (m_frameActive)
            {
                throw new InvalidOperationException("BeginFrame was called while a frame is already active.");
            }

            m_frameActive = true;
            m_frameJobs.Clear();
        }
    }

    /// <summary>
    /// Commits the current frame-scoped operation and returns its completion identity.
    /// </summary>
    public void EndFrame()
    {
        ThrowIfDisposed();

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

        List<Exception>? exceptions = null;
        for (var i = 0; i < handles.Count; i++)
        {
            try
            {
                Complete(handles[i]);
            }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
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

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more jobs failed during EndFrame.", exceptions);
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

        List<int>? readyIndices = null;
        JobHandle handle;
        lock (m_jobsGate)
        {
            EnsureFrameActiveNoLock();

            foreach (JobHandle dependency in dependencies)
            {
                if (!dependency.isValid) throw new ArgumentException("Dependency handle is invalid.", nameof(dependencies));
                _ = ResolveHandleNoLock(dependency);
            }
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

        ThrowIfDisposed();
        lock (m_jobsGate)
        {
            EnsureFrameActiveNoLock();
            var chunkCount = 1 + (length - 1) / batchSize;
            RequireCapacityNoLock(checked(chunkCount + 1));
            var chunkHandles = new JobHandle[chunkCount];
            var chunkIndex = 0;
            for (var start = 0; start < length;)
            {
                var end = start + Math.Min(batchSize, length - start);
                var rangeState = new ParallelForRangeState(body, start, end);
                chunkHandles[chunkIndex++] = Schedule(s_parallelRangeInvoker, rangeState, ReadOnlySpan<JobHandle>.Empty);
                start = end;
            }

            return CombineDependencies(chunkHandles);
        }
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

        var spinner = new SpinWait();
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

            if (TryExecuteOneAvailableJob())
            {
                spinner.Reset();
                continue;
            }

            if (spinner.Count < 12)
            {
                spinner.SpinOnce();
            }
            else
            {
                m_completionSignal.Wait(1);
                m_completionSignal.Reset();
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
        if (!m_mainThreadQueue.TryEnqueue(action))
            throw new InvalidOperationException("The main-thread callback queue is at capacity or retiring.");
    }

    /// <summary>
    /// Executes every queued main-thread callback in submission order.
    /// </summary>
    public void DrainMainThreadQueue()
    {
        ThrowIfDisposed();
        EnsureMainThread();

        m_mainThreadQueue.Drain();
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        EnsureMainThread();
        try
        {
            if (m_frameActive)
                EndFrame();
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception) { m_shutdownFailures.Add(exception); }
        try { m_mainThreadQueue.Close(); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception) { m_shutdownFailures.Add(exception); }
        m_disposed = true;
        Interlocked.Exchange(ref m_running, 0);

        for (var i = 0; i < m_workers.Length; i++)
        {
            m_wakeSignal.Release();
        }

        for (var i = 0; i < m_workers.Length; i++)
        {
            m_workers[i].thread.Join();
        }

        m_currentWorkerId.Dispose();
        m_wakeSignal.Dispose();
        m_completionSignal.Dispose();
        m_mainThreadQueue.Close();
        m_globalInjectQueue.Clear();
        m_jobs.Clear();
        m_frameJobs.Clear();
        m_freeIndices.Clear();
        if (m_shutdownFailures.Count > 0)
        {
            var failure = new AggregateException("Job retirement completed with failed frame work.", m_shutdownFailures);
            m_shutdownFailures.Clear();
            throw failure;
        }
    }

    private void WorkerLoop(int workerId)
    {
        m_currentWorkerId.Value = workerId;
        while (Volatile.Read(ref m_running) != 0)
        {
            m_wakeSignal.Wait(50);
            if (Volatile.Read(ref m_running) == 0)
            {
                break;
            }

            while (TryExecuteOneAvailableJob())
            {
            }
        }
    }

    private bool TryExecuteOneAvailableJob()
    {
        if (!TryDequeueWorkItem(out var jobIndex))
        {
            return false;
        }

        Action<object?>? callback = null;
        object? state = null;
        lock (m_jobsGate)
        {
            if (!TryGetActiveRecordNoLock(jobIndex, out var record))
            {
                return false;
            }

            if (record.executionState != JobExecutionState.Queued)
            {
                return false;
            }

            record.executionState = JobExecutionState.Running;
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
            if (!TryGetActiveRecordNoLock(jobIndex, out var record))
            {
                return true;
            }

            record.executionState = JobExecutionState.Completed;
            record.exception = fault;
            if (fault is null || RetirementPendingException.Find(fault) is null)
            {
                record.callback = null;
                record.state = null;
            }

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

        m_completionSignal.Set();
        return true;
    }

    private bool TryDequeueWorkItem(out int jobIndex)
    {
        var workerId = m_currentWorkerId.Value;
        if (workerId >= 0)
        {
            if (m_workers[workerId].localQueue.TryPopBottom(out jobIndex))
            {
                return true;
            }
        }

        if (m_globalInjectQueue.TryDequeue(out jobIndex))
        {
            return true;
        }

        if (workerId >= 0)
        {
            var startVictim = (workerId + 1) % m_workers.Length;
            for (var i = 0; i < m_workers.Length - 1; i++)
            {
                var victim = (startVictim + i) % m_workers.Length;
                if (m_workers[victim].localQueue.TryStealTop(out jobIndex))
                {
                    return true;
                }
            }
        }
        else
        {
            for (var i = 0; i < m_workers.Length; i++)
            {
                if (m_workers[i].localQueue.TryStealTop(out jobIndex))
                {
                    return true;
                }
            }
        }

        jobIndex = default;
        return false;
    }

    private void QueueReadyJobs(List<int> readyIndices)
    {
        var workerId = m_currentWorkerId.Value;
        for (var i = 0; i < readyIndices.Count; i++)
        {
            var index = readyIndices[i];
            if (workerId >= 0)
            {
                m_workers[workerId].localQueue.PushBottom(index);
            }
            else
            {
                m_globalInjectQueue.Enqueue(index);
            }

            m_wakeSignal.Release();
        }
    }

    private void RequireCapacityNoLock(int count)
    {
        if (count > m_maxFrameJobs - m_frameJobs.Count)
        {
            m_rejectedJobs += count;
            throw new InvalidOperationException("The frame job capacity has been reached.");
        }
    }

    private int AllocateJobIndexNoLock(Action<object?> callback, object? state)
    {
        RequireCapacityNoLock(1);
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
        m_peakFrameJobs = Math.Max(m_peakFrameJobs, m_frameJobs.Count);
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
            throw new ObjectDisposedException(nameof(WorkStealingJobSystem));
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
