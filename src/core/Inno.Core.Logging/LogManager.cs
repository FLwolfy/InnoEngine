using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Logging;

public static class LogManager
{
    private const string C_WORKER_THREAD_NAME = "Inno.LogManager.Worker";
    
    private static readonly List<ILogSink> SINKS = [];
    private static readonly Lock SINKS_LOCK = new();
    private static readonly ConcurrentQueue<LogEntry> QUEUE = new();
    private static readonly SemaphoreSlim SIGNAL = new(0);

    private static Thread? m_workerThread;
    private static volatile bool m_running;
    private static volatile LogLevel m_minimumLevel = LogLevel.Debug;

    public static void Initialize()
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

    public static void RegisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (SINKS_LOCK)
        {
            SINKS.Add(sink);
        }
    }
    
    public static void UnregisterSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (SINKS_LOCK)
        {
            SINKS.Remove(sink);
        }
    }

    public static void SetMinimumLevel(LogLevel level)
    {
        m_minimumLevel = level;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEnabled(LogLevel level)
        => level >= m_minimumLevel;

    public static void Dispatch(LogEntry entry)
    {
        if (!IsEnabled(entry.level))
            return;

        QUEUE.Enqueue(entry);
        SIGNAL.Release();
    }

    private static void ProcessQueue()
    {
        while (m_running)
        {
            SIGNAL.Wait();
            DrainQueue();
        }

        DrainQueue();
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

    public static void Shutdown()
    {
        if (m_workerThread == null)
            return;

        m_running = false;
        SIGNAL.Release();

        m_workerThread.Join();
        m_workerThread = null;

        DrainQueue();
    }
}
