using System.Collections.Generic;

using Inno.Core.Logging;

namespace Inno.Editor.Core;

/// <summary>
/// Thread-safe rolling buffer for log entries.
/// </summary>
public sealed class EditorLogBuffer : ILogSink
{
    private readonly Queue<LogEntry> m_entries = new();
    private readonly object m_sync = new();
    private int m_capacity = 1024;

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
                m_capacity = value < 16 ? 16 : value;
                TrimExcessUnsafe();
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
            m_entries.Enqueue(entry);
            TrimExcessUnsafe();
        }
    }

    /// <summary>
    /// Returns a snapshot of buffered entries.
    /// </summary>
    public LogEntry[] Snapshot()
    {
        lock (m_sync)
        {
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
        }
    }

    private void TrimExcessUnsafe()
    {
        while (m_entries.Count > m_capacity)
        {
            _ = m_entries.Dequeue();
        }
    }
}
