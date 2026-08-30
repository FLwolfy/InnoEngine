using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Inno.Core.Assemblies;
using Inno.Core.Logging;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Thread-safe rolling buffer for log entries.
/// </summary>
internal sealed class EditorLogBuffer : ILogSink
{
    private readonly Queue<BufferedLogEntry> m_entries = new();
    private readonly object m_sync = new();
    private int m_capacity = 1024;
    private long m_nextId;
    private long? m_playSessionStartId;
    private long m_version;

    /// <summary>
    /// Gets or sets maximum retained log entry count.
    /// </summary>
    public int capacity
    {
        get => m_capacity;
        set
        {
            lock (m_sync)
            {
                int normalizedCapacity = value < 16 ? 16 : value;
                if (m_capacity == normalizedCapacity)
                    return;
                m_capacity = normalizedCapacity;
                TrimExcessUnsafe();
                m_version++;
            }
        }
    }

    /// <summary>
    /// Appends one log entry.
    /// </summary>
    /// <param name="entry">Entry to append.</param>
    public void Receive(LogEntry entry)
    {
        lock (m_sync)
        {
            m_entries.Enqueue(new BufferedLogEntry(
                Interlocked.Increment(ref m_nextId),
                entry));
            TrimExcessUnsafe();
            m_version++;
        }
    }

    /// <summary>
    /// Returns a snapshot of buffered entries.
    /// </summary>
    /// <returns>A stable copy of the currently buffered entries in arrival order.</returns>
    public LogEntry[] Snapshot()
    {
        return Snapshot(out _);
    }

    /// <summary>
    /// Returns a snapshot of buffered entries and its monotonic content version.
    /// </summary>
    /// <param name="version">Receives the version associated with the returned snapshot.</param>
    /// <returns>A stable copy of the currently buffered entries.</returns>
    public LogEntry[] Snapshot(out long version)
    {
        lock (m_sync)
        {
            version = m_version;
            return m_entries.Select(static entry => entry.entry).ToArray();
        }
    }

    internal BufferedLogEntry[] SnapshotBuffered(out long version)
    {
        lock (m_sync)
        {
            version = m_version;
            return m_entries.ToArray();
        }
    }

    /// <summary>
    /// Clears all buffered entries.
    /// </summary>
    public void Clear()
    {
        lock (m_sync)
        {
            m_entries.Clear();
            m_version++;
        }
    }

    internal void BeginPlaySession()
    {
        lock (m_sync)
        {
            if (m_playSessionStartId is not null)
                throw new InvalidOperationException("The Console already owns an active Play Mode log session.");
            m_playSessionStartId = m_nextId;
        }
    }

    internal int CompletePlaySession()
    {
        lock (m_sync)
        {
            if (m_playSessionStartId is not long startId)
                return 0;

            int removed = 0;
            int count = m_entries.Count;
            for (int i = 0; i < count; i++)
            {
                BufferedLogEntry buffered = m_entries.Dequeue();
                if (buffered.id > startId && IsTransientRuntimeEntry(buffered.entry))
                {
                    removed++;
                    continue;
                }
                m_entries.Enqueue(buffered);
            }

            m_playSessionStartId = null;
            if (removed != 0)
                m_version++;
            return removed;
        }
    }

    internal void CancelPlaySession()
    {
        lock (m_sync)
            m_playSessionStartId = null;
    }

    private static bool IsTransientRuntimeEntry(LogEntry entry)
        => entry.scope == AssemblyScope.Runtime && entry.level < LogLevel.Warn;

    private void TrimExcessUnsafe()
    {
        while (m_entries.Count > m_capacity)
        {
            _ = m_entries.Dequeue();
        }
    }
}
