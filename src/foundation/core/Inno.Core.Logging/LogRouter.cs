using Inno.Core.Execution;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Inno.Extensibility.Modules;

namespace Inno.Core.Logging;

/// <summary>
/// Owns one host's asynchronous log queue, filtering policy, worker, and sink collection.
/// </summary>
public sealed class LogRouter : IDisposable
{
    private static readonly ExecutionSlot<LogRouter> S_CURRENT_SCOPE = new("router");

    private readonly List<ILogSink> m_sinks = [];
    private readonly Lock m_sinksLock = new();
    private readonly ConcurrentQueue<WorkItem> m_queue = new();
    private readonly object m_queueSync = new();
    private readonly int m_queueCapacity;
    private readonly int m_drainBudget;
    private readonly SemaphoreSlim m_signal = new(0);
    private readonly Thread m_worker;
    private volatile bool m_running = true;
    private volatile LogLevel m_minimumLevel = LogLevel.Debug;
    private bool m_disposed;

    /// <summary>
    /// Occurs after a failing sink has been quarantined from this router.
    /// </summary>
    public event Action<ILogSink, Exception>? sinkFailed;

    /// <summary>
    /// Creates and starts an isolated asynchronous logging router.
    /// </summary>
    /// <param name="queueCapacity">
    /// Maximum pending entries and flush barriers before explicit rejection.
    /// </param>
    /// <param name="drainBudget">
    /// Maximum work items delivered using one sink snapshot.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A capacity or budget is not positive.
    /// </exception>
    public LogRouter(int queueCapacity = 65536, int drainBudget = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(drainBudget);
        m_queueCapacity = queueCapacity;
        m_drainBudget = drainBudget;
        m_worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = $"Inno.LogRouter.{Guid.NewGuid():N}"
        };
        m_worker.Start();
    }

    internal static LogRouter current
        => S_CURRENT_SCOPE.current;

    /// <summary>
    /// Binds this router to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public IDisposable EnterScope()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return S_CURRENT_SCOPE.Enter(this);
    }

    /// <summary>
    /// Registers a sink to receive future entries from this router.
    /// </summary>
    /// <param name="sink">
    /// The sink to register exactly once.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sink"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public void RegisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sinksLock)
        {
            if (!m_sinks.Contains(sink))
                m_sinks.Add(sink);
        }
    }

    /// <summary>
    /// Unregisters a sink so it receives no future entries from this router.
    /// </summary>
    /// <param name="sink">
    /// The sink to remove when registered.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sink"/> is null.
    /// </exception>
    public void UnregisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (m_sinksLock)
            m_sinks.Remove(sink);
    }

    /// <summary>
    /// Sets the lowest severity accepted by this router.
    /// </summary>
    /// <param name="level">
    /// The minimum dispatched severity.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public void SetMinimumLevel(LogLevel level)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_minimumLevel = level;
    }

    /// <summary>
    /// Creates a category-bound logger for one engine service or extension type.
    /// </summary>
    /// <typeparam name="TOwner">
    /// The type whose assembly ownership and category identify emitted entries.
    /// </typeparam>
    /// <returns>
    /// A stateless logger bound to this router and the supplied owner type.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public Logger CreateLogger<TOwner>()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        Type ownerType = typeof(TOwner);
        return new Logger(
            this,
            ownerType.Assembly.GetInnoAssemblyDomain(),
            ownerType.Assembly.GetInnoAssemblyScope(),
            ownerType.Name);
    }

    /// <summary>
    /// Enqueues an immutable entry for asynchronous delivery.
    /// </summary>
    /// <param name="entry">
    /// The entry to dispatch when it satisfies the current severity policy.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public void Dispatch(LogEntry entry)
    {
        if (!TryDispatch(entry))
            throw new InvalidOperationException("The log queue is full; the producer must apply backpressure.");
    }

    /// <summary>
    /// Attempts to enqueue an immutable log entry without blocking its producer.
    /// </summary>
    /// <param name="entry">
    /// The message to accept under the current severity policy.
    /// </param>
    /// <returns>
    /// True when accepted or filtered by policy; false when bounded queue capacity is exhausted.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// The router has stopped accepting work.
    /// </exception>
    public bool TryDispatch(LogEntry entry)
    {
        lock (m_queueSync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (!IsEnabled(entry.level))
                return true;
            if (m_queue.Count >= m_queueCapacity)
                return false;
            Enqueue(WorkItem.ForEntry(entry));
            return true;
        }
    }

    /// <summary>
    /// Blocks until every entry enqueued before this call has reached the current sink snapshot.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the logging worker attempts to wait for itself.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this router has been disposed.
    /// </exception>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (ReferenceEquals(Thread.CurrentThread, m_worker))
            throw new InvalidOperationException("The logging worker cannot wait for itself.");
        using var completion = new ManualResetEventSlim(initialState: false);
        lock (m_queueSync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_queue.Count >= m_queueCapacity)
                throw new InvalidOperationException("The log queue is full; retry the flush after the worker drains pending entries.");
            Enqueue(WorkItem.ForBarrier(completion));
        }
        completion.Wait();
    }

    /// <summary>
    /// Drains pending entries, stops the worker, and disposes every sink still owned by this router.
    /// </summary>
    public void Dispose()
    {
        if (ReferenceEquals(Thread.CurrentThread, m_worker))
            throw new InvalidOperationException("The logging worker cannot dispose its own router.");
        lock (m_queueSync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_running = false;
            m_signal.Release();
        }
        m_worker.Join();
        DrainQueue();
        ILogSink[] sinks;
        lock (m_sinksLock)
        {
            sinks = m_sinks.ToArray();
            m_sinks.Clear();
            sinkFailed = null;
        }
        List<Exception>? failures = null;
        for (int index = 0; index < sinks.Length; index++)
        {
            if (sinks[index] is not IDisposable disposable)
                continue;
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        m_signal.Dispose();
        if (failures is not null)
            throw new AggregateException("One or more log sinks failed to dispose.", failures);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsEnabled(LogLevel level) => level >= m_minimumLevel;

    private void ProcessQueue()
    {
        while (true)
        {
            m_signal.Wait();
            DrainQueue();
            if (!m_running && m_queue.IsEmpty)
                return;
        }
    }

    private void Enqueue(WorkItem item)
    {
        m_queue.Enqueue(item);
        m_signal.Release();
    }

    private void DrainQueue()
    {
        ILogSink[] sinks;
        lock (m_sinksLock)
            sinks = m_sinks.ToArray();
        var quarantined = new HashSet<ILogSink>();
        int remaining = m_drainBudget;
        while (remaining-- > 0 && m_queue.TryDequeue(out WorkItem item))
        {
            if (item.completion is ManualResetEventSlim completion)
            {
                completion.Set();
                continue;
            }
            for (int index = 0; index < sinks.Length; index++)
            {
                if (quarantined.Contains(sinks[index]))
                    continue;
                try
                {
                    sinks[index].Receive(item.entry);
                }
                catch (Exception exception)
                {
                    quarantined.Add(sinks[index]);
                    lock (m_sinksLock)
                        m_sinks.Remove(sinks[index]);
                    ReportSinkFailure(sinks[index], exception);
                }
            }
        }
    }

    private void ReportSinkFailure(ILogSink sink, Exception exception)
    {
        Action<ILogSink, Exception>? handlers = sinkFailed;
        if (handlers is null)
        {
            Console.Error.WriteLine(
                $"Log sink '{sink.GetType().FullName}' failed and was quarantined: {exception}");
            return;
        }
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ILogSink, Exception>)handler)(sink, exception);
            }
            catch (Exception observerFailure)
            {
                Console.Error.WriteLine(
                    $"Log sink failure observer '{handler.Method.DeclaringType?.FullName}' failed: {observerFailure}");
            }
        }
    }

    private readonly record struct WorkItem(LogEntry entry, ManualResetEventSlim? completion)
    {
        internal static WorkItem ForEntry(LogEntry entry) => new(entry, null);

        internal static WorkItem ForBarrier(ManualResetEventSlim completion) => new(default, completion);
    }

}
