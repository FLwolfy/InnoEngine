using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Logging;

/// <summary>
/// Central log dispatcher that routes entries to registered sinks on a background worker.
/// </summary>
public static class LogManager
{
    private const string C_WORKER_THREAD_NAME = "Inno.LogManager.Worker";

    private static readonly List<ILogSink> SINKS = [];
    private static readonly Lock SINKS_LOCK = new();
    private static readonly ConcurrentQueue<WorkItem> QUEUE = new();
    private static readonly SemaphoreSlim SIGNAL = new(0);
    private static readonly Lock LIFECYCLE_LOCK = new();

    private static Thread? s_workerThread;
    private static volatile bool s_running;
    private static volatile LogLevel s_minimumLevel = LogLevel.Debug;

    /// <summary>
    /// Starts the logging worker thread if it has not been started.
    /// </summary>
    public static void Initialize()
    {
        lock (LIFECYCLE_LOCK)
        {
            if (s_workerThread != null)
                return;

            s_running = true;
            s_workerThread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = C_WORKER_THREAD_NAME
            };
            s_workerThread.Start();
        }
    }

    /// <summary>
    /// Registers a sink to receive future log entries.
    /// </summary>
    /// <param name="sink">The sink to register.</param>
    public static void RegisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (SINKS_LOCK)
        {
            SINKS.Add(sink);
        }
    }

    /// <summary>
    /// Unregisters a sink so it no longer receives log entries.
    /// </summary>
    /// <param name="sink">The sink to remove.</param>
    public static void UnregisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (SINKS_LOCK)
        {
            SINKS.Remove(sink);
        }
    }

    /// <summary>
    /// Sets the minimum log level that will be dispatched.
    /// </summary>
    /// <param name="level">The minimum enabled level.</param>
    public static void SetMinimumLevel(LogLevel level)
    {
        s_minimumLevel = level;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEnabled(LogLevel level)
        => level >= s_minimumLevel;

    /// <summary>
    /// Enqueues a log entry for asynchronous dispatch.
    /// </summary>
    /// <param name="entry">The entry to dispatch.</param>
    public static void Dispatch(LogEntry entry)
    {
        if (!IsEnabled(entry.level))
            return;

        QUEUE.Enqueue(WorkItem.ForEntry(entry));
        SIGNAL.Release();
    }

    /// <summary>
    /// Blocks until every log entry enqueued before this call has been delivered to the registered sinks.
    /// </summary>
    /// <remarks>
    /// This synchronization boundary is intended for infrequent lifecycle transitions. Regular logging
    /// remains asynchronous.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called from the logging worker, where waiting for that same worker would deadlock.
    /// </exception>
    public static void Flush()
    {
        using var completion = new ManualResetEventSlim(initialState: false);
        bool drainSynchronously;
        lock (LIFECYCLE_LOCK)
        {
            if (ReferenceEquals(Thread.CurrentThread, s_workerThread))
                throw new InvalidOperationException("The logging worker cannot wait for itself to flush.");
            drainSynchronously = s_workerThread is null;
            QUEUE.Enqueue(WorkItem.ForBarrier(completion));
            SIGNAL.Release();
        }

        if (drainSynchronously)
            DrainQueue();
        completion.Wait();
    }

    private static void ProcessQueue()
    {
        while (true)
        {
            SIGNAL.Wait();
            DrainQueue();

            if (!s_running && QUEUE.IsEmpty)
                break;
        }
    }

    private static void DrainQueue()
    {
        ILogSink[] sinksSnapshot;

        lock (SINKS_LOCK)
        {
            if (SINKS.Count == 0)
            {
                while (QUEUE.TryDequeue(out WorkItem item))
                    item.completion?.Set();
                return;
            }

            sinksSnapshot = SINKS.ToArray();
        }

        while (QUEUE.TryDequeue(out WorkItem item))
        {
            if (item.completion is ManualResetEventSlim completion)
            {
                completion.Set();
                continue;
            }

            foreach (ILogSink sink in sinksSnapshot)
            {
                try
                {
                    sink.Receive(item.entry);
                }
                catch
                {
                    // Isolate sink failure
                }
            }
        }
    }

    private readonly record struct WorkItem(LogEntry entry, ManualResetEventSlim? completion)
    {
        internal static WorkItem ForEntry(LogEntry entry) => new(entry, null);

        internal static WorkItem ForBarrier(ManualResetEventSlim completion) => new(default, completion);
    }

    /// <summary>
    /// Stops the logging worker, drains pending entries, and disposes registered disposable sinks.
    /// </summary>
    public static void Shutdown()
    {
        Thread? worker;
        ILogSink[] sinksToDispose;

        lock (LIFECYCLE_LOCK)
        {
            if (s_workerThread == null)
                return;

            s_running = false;
            SIGNAL.Release();

            worker = s_workerThread;
            s_workerThread = null;
        }

        worker.Join();
        DrainQueue();

        lock (SINKS_LOCK)
        {
            sinksToDispose = SINKS.ToArray();
            SINKS.Clear();
        }

        for (var i = 0; i < sinksToDispose.Length; i++)
        {
            if (sinksToDispose[i] is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // Isolate sink disposal failure
                }
            }
        }
    }
}
