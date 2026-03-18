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
    private static readonly ConcurrentQueue<LogEntry> QUEUE = new();
    private static readonly SemaphoreSlim SIGNAL = new(0);
    private static readonly Lock LIFECYCLE_LOCK = new();

    private static Thread? m_workerThread;
    private static volatile bool m_running;
    private static volatile LogLevel m_minimumLevel = LogLevel.Debug;

    /// <summary>
    /// Starts the logging worker thread if it has not been started.
    /// </summary>
    public static void Initialize()
    {
        lock (LIFECYCLE_LOCK)
        {
            if (m_workerThread != null)
                return;

            m_running = true;
            m_workerThread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = C_WORKER_THREAD_NAME
            };
            m_workerThread.Start();
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
        m_minimumLevel = level;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEnabled(LogLevel level)
        => level >= m_minimumLevel;

    /// <summary>
    /// Enqueues a log entry for asynchronous dispatch.
    /// </summary>
    /// <param name="entry">The entry to dispatch.</param>
    public static void Dispatch(LogEntry entry)
    {
        if (!IsEnabled(entry.level))
            return;

        QUEUE.Enqueue(entry);
        SIGNAL.Release();
    }

    private static void ProcessQueue()
    {
        while (true)
        {
            SIGNAL.Wait();
            DrainQueue();

            if (!m_running && QUEUE.IsEmpty)
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
                while (QUEUE.TryDequeue(out _)) { }
                return;
            }

            sinksSnapshot = SINKS.ToArray();
        }

        while (QUEUE.TryDequeue(out var entry))
        {
            foreach (var sink in sinksSnapshot)
            {
                try
                {
                    sink.Receive(entry);
                }
                catch
                {
                    // Isolate sink failure
                }
            }
        }
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
            if (m_workerThread == null)
                return;

            m_running = false;
            SIGNAL.Release();

            worker = m_workerThread;
            m_workerThread = null;
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
