using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Inno.Core.Execution;

/// <summary>
/// Owns cancellation, tracked work and reverse-order resource retirement for one lifecycle.
/// </summary>
public sealed class LifetimeScope : IDisposable, IAsyncDisposable
{
    private readonly object m_sync = new();
    private readonly CancellationTokenSource m_cancellation = new();
    private readonly List<IDisposable> m_resources = [];
    private readonly List<Task> m_tasks = [];
    private readonly List<Exception> m_retirementFailures = [];
    private Exception? m_blockedWorkFailure;
    private bool m_canceling;
    private bool m_disposing;
    private bool m_stopping;
    private bool m_disposed;
    private readonly int m_maxTrackedWork;
    private int m_peakTrackedWork;
    private long m_rejectedWork;

    /// <summary>
    /// Creates a lifetime with finite asynchronous admission capacity.
    /// </summary>
    /// <param name="maxTrackedWork">
    /// Maximum retained work records; completed successful work releases capacity immediately.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The capacity is not positive.
    /// </exception>
    public LifetimeScope(int maxTrackedWork = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTrackedWork);
        m_maxTrackedWork = maxTrackedWork;
    }

    /// <summary>
    /// Gets retained work records, including faults awaiting final retirement reporting.
    /// </summary>
    public int trackedWorkCount { get { lock (m_sync) return m_tasks.Count(static task => !task.IsCompletedSuccessfully && !task.IsCanceled); } }
    /// <summary>
    /// Gets the largest simultaneous retained work count.
    /// </summary>
    public int peakTrackedWorkCount { get { lock (m_sync) return m_peakTrackedWork; } }
    /// <summary>
    /// Gets work registrations rejected before ownership transfer by finite capacity.
    /// </summary>
    public long rejectedWorkCount { get { lock (m_sync) return m_rejectedWork; } }

    /// <summary>
    /// Gets cancellation shared by work owned by this lifetime.
    /// </summary>
    public CancellationToken cancellationToken => m_cancellation.Token;

    /// <summary>
    /// Gets whether all tracked work has completed and resources can retire synchronously.
    /// </summary>
    public bool isQuiescent
    {
        get
        {
            lock (m_sync)
                return !m_canceling && !m_disposing && m_blockedWorkFailure is null && m_tasks.All(static task =>
                    task.IsCompleted && (task.Exception is not Exception failure || RetirementPendingException.Find(failure) is null));
        }
    }

    /// <summary>
    /// Transfers disposal ownership of a resource to this lifetime.
    /// </summary>
    /// <typeparam name="TResource">
    /// The disposable resource type.
    /// </typeparam>
    /// <param name="resource">
    /// The resource to release in reverse registration order.
    /// </param>
    /// <returns>
    /// The same resource for explicit dependency wiring.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The resource is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Retirement has already started.
    /// </exception>
    public TResource Own<TResource>(TResource resource) where TResource : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (m_sync)
        {
            EnsureAccepting();
            m_resources.Add(resource);
        }
        return resource;
    }

    /// <summary>
    /// Tracks work that must complete before this lifetime's resources can be released.
    /// </summary>
    /// <param name="task">
    /// Work that observes this lifetime's cancellation token.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The task is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Retirement has started or work capacity is exhausted; ownership remains with the caller.
    /// </exception>
    public void Track(Task task) => TrackCore(task, observeCompletion: true);

    /// <summary>
    /// Atomically admits and tracks asynchronous work under this owner's cancellation boundary.
    /// </summary>
    /// <typeparam name="TResult">
    /// The operation result type.
    /// </typeparam>
    /// <param name="operation">
    /// The operation, which must observe the supplied token and must not require owner-thread completion.
    /// </param>
    /// <param name="cancellationToken">
    /// Additional cancellation requested by the caller.
    /// </param>
    /// <returns>
    /// The tracked result, cancellation or operation failure.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Retirement has begun or work capacity is exhausted; the operation is not invoked.
    /// </exception>
    public Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrackCore(completion.Task, observeCompletion: false);
        _ = CompleteOperationAsync(operation, cancellationToken, completion);
        return completion.Task;
    }

    /// <summary>
    /// Stops new registrations and invokes cancellation callbacks on the calling thread.
    /// </summary>
    /// <remarks>
    /// Callbacks must return promptly. Dependent resources remain owned while cancellation callbacks execute,
    /// including when another thread attempts disposal. A callback reporting pending work permanently blocks release.
    /// </remarks>
    /// <exception cref="AggregateException">
    /// One or more cancellation callbacks fail.
    /// </exception>
    public void Cancel()
    {
        lock (m_sync)
        {
            if (m_blockedWorkFailure is not null)
                throw m_blockedWorkFailure;
            if (m_disposed || m_stopping)
                return;
            m_stopping = true;
            m_canceling = true;
        }
        try { m_cancellation.Cancel(); }
        catch (Exception exception) when (RetirementPendingException.Find(exception) is not null)
        {
            lock (m_sync)
                m_blockedWorkFailure = exception;
            throw;
        }
        catch (Exception exception)
        {
            lock (m_sync)
                RetirementPendingException.CollectCompletedFailures(exception, m_retirementFailures);
            throw;
        }
        finally
        {
            lock (m_sync)
                m_canceling = false;
        }
    }

    /// <summary>
    /// Releases a quiescent lifetime, attempting every resource even when release fails.
    /// </summary>
    /// <remarks>
    /// Concurrent or reentrant retirement reports pending work without invoking a resource twice.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Tracked work remains active; drain it before retrying.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Cancellation, work or resource retirement fails.
    /// </exception>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_blockedWorkFailure is not null)
                throw m_blockedWorkFailure;
            if (m_disposed)
                return;
            if (m_disposing)
                throw new RetirementPendingException("Another retirement attempt still owns this lifetime's resources.");
            m_disposing = true;
        }
        try
        {
            try { Cancel(); }
            catch (Exception exception) when (RetirementPendingException.Find(exception) is not null) { throw; }
            catch (Exception) { /* Cancel retained ordinary callback failures for the final report. */ }
            RetireResources();
        }
        finally
        {
            lock (m_sync)
                m_disposing = false;
        }
    }

    /// <summary>
    /// Cancels and asynchronously drains tracked work before reverse-order release.
    /// </summary>
    /// <remarks>
    /// Only use asynchronous disposal for thread-neutral resources. Control-thread owners must await their work
    /// and call synchronous disposal at their own safe point; this method does not marshal to an owner thread.
    /// </remarks>
    /// <returns>
    /// A task that completes only after work and resource retirement finish.
    /// </returns>
    /// <exception cref="AggregateException">
    /// Cancellation, work or resource retirement fails.
    /// </exception>
    public async ValueTask DisposeAsync()
    {
        try { Cancel(); }
        catch (Exception exception) when (RetirementPendingException.Find(exception) is not null) { throw; }
        catch (Exception) { /* Cancel retained ordinary callback failures for the final report. */ }
        Task[] pending;
        lock (m_sync)
            pending = m_tasks.ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch { /* Dispose observes every fault after all work has completed. */ }
        Dispose();
    }

    private void RetireResources()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            if (m_canceling || m_tasks.Any(static task => !task.IsCompleted))
                throw new RetirementPendingException("Lifetime work is still active. Drain it before releasing its resources.");
            foreach (Task task in m_tasks)
            {
                if (task.Exception is not null)
                {
                    if (RetirementPendingException.Find(task.Exception) is not null)
                    {
                        m_blockedWorkFailure = task.Exception;
                        throw m_blockedWorkFailure;
                    }
                    m_retirementFailures.Add(task.Exception);
                }
            }
            m_tasks.Clear();
        }
        while (true)
        {
            IDisposable resource;
            lock (m_sync)
            {
                if (m_resources.Count == 0)
                    break;
                resource = m_resources[^1];
            }
            try { resource.Dispose(); }
            catch (Exception exception) when (RetirementPendingException.Find(exception) is not null)
            {
                RetirementPendingException.CollectCompletedFailures(exception, m_retirementFailures);
                throw;
            }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
            lock (m_sync)
                m_resources.RemoveAt(m_resources.Count - 1);
        }
        lock (m_sync)
            m_disposed = true;
        m_cancellation.Dispose();
        if (m_retirementFailures.Count > 0)
        {
            var failure = new AggregateException("Lifetime retirement failed after all resources were attempted.", m_retirementFailures);
            m_retirementFailures.Clear();
            throw failure;
        }
    }

    private void TrackCore(Task task, bool observeCompletion)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (m_sync)
        {
            EnsureAccepting();
            m_tasks.RemoveAll(static completed => completed.IsCompletedSuccessfully || completed.IsCanceled);
            if (m_tasks.Contains(task)) return;
            if (m_tasks.Count >= m_maxTrackedWork)
            {
                m_rejectedWork++;
                throw new InvalidOperationException("The lifetime's asynchronous work capacity has been reached; ownership was not transferred.");
            }
            m_tasks.Add(task);
            m_peakTrackedWork = Math.Max(m_peakTrackedWork, m_tasks.Count);
            if (!observeCompletion) return;
            if (ExecutionContext.IsFlowSuppressed()) ObserveCompletion(task);
            else
            {
                using AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                ObserveCompletion(task);
            }
        }
    }

    private void ObserveCompletion(Task task)
        => _ = task.ContinueWith(static (completed, state) =>
        {
            if (!completed.IsCompletedSuccessfully && !completed.IsCanceled) return;
            var owner = (LifetimeScope)state!;
            lock (owner.m_sync) owner.m_tasks.Remove(completed);
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void EnsureAccepting()
    {
        if (m_stopping || m_disposed)
            throw new InvalidOperationException("A retiring lifetime cannot accept resources or work.");
    }

    private async Task CompleteOperationAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken,
        TaskCompletionSource<TResult> completion)
    {
        try
        {
            TResult result;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_cancellation.Token))
                result = await operation(linked.Token).ConfigureAwait(false);
            lock (m_sync)
            {
                completion.SetResult(result);
                m_tasks.Remove(completion.Task);
            }
        }
        catch (Exception exception) when (RetirementPendingException.Find(exception) is not null)
        {
            completion.SetException(exception);
        }
        catch (OperationCanceledException exception)
        {
            lock (m_sync)
            {
                completion.SetCanceled(exception.CancellationToken);
                m_tasks.Remove(completion.Task);
            }
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }
}
