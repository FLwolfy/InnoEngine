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
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    private readonly List<ILogSink> m_sinks = [];
    private readonly Lock m_sinksLock = new();
    private readonly ConcurrentQueue<WorkItem> m_queue = new();
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
    public LogRouter()
    {
        m_worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = $"Inno.LogRouter.{Guid.NewGuid():N}"
        };
        m_worker.Start();
    }

    internal static LogRouter current
        => S_CURRENT_SCOPE.Value?.router
            ?? throw new InvalidOperationException(
                "No log router is bound to the current runtime execution context.");

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
        var scope = new Scope(this, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
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
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!IsEnabled(entry.level))
            return;
        m_queue.Enqueue(WorkItem.ForEntry(entry));
        m_signal.Release();
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
        m_queue.Enqueue(WorkItem.ForBarrier(completion));
        m_signal.Release();
        completion.Wait();
    }

    /// <summary>
    /// Drains pending entries, stops the worker, and disposes every sink still owned by this router.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_running = false;
        m_signal.Release();
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

    private void DrainQueue()
    {
        ILogSink[] sinks;
        lock (m_sinksLock)
            sinks = m_sinks.ToArray();
        var quarantined = new HashSet<ILogSink>();
        while (m_queue.TryDequeue(out WorkItem item))
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

    private sealed class Scope(LogRouter router, Scope? previous) : IDisposable
    {
        private bool m_disposed;

        internal LogRouter router { get; } = router;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
                throw new InvalidOperationException("Log router scopes must be disposed in last-in-first-out order.");
            m_disposed = true;
            S_CURRENT_SCOPE.Value = previous;
        }
    }
}
