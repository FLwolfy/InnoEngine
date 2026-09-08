using System;

namespace Inno.Core.Jobs.Internal;

internal sealed class WorkStealingDeque<T>
{
    private readonly object m_gate = new();
    private T[] m_buffer;
    private int m_head;
    private int m_tail;

    internal WorkStealingDeque(int initialCapacity = 64)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "initialCapacity must be greater than zero.");
        }

        m_buffer = new T[initialCapacity];
    }

    internal void PushBottom(T value)
    {
        lock (m_gate)
        {
            if (CountNoLock() == m_buffer.Length)
            {
                GrowNoLock();
            }

            m_buffer[m_tail] = value;
            m_tail = (m_tail + 1) % m_buffer.Length;
        }
    }

    internal bool TryPopBottom(out T value)
    {
        lock (m_gate)
        {
            if (m_head == m_tail)
            {
                value = default!;
                return false;
            }

            m_tail = (m_tail - 1 + m_buffer.Length) % m_buffer.Length;
            value = m_buffer[m_tail];
            m_buffer[m_tail] = default!;
            return true;
        }
    }

    internal bool TryStealTop(out T value)
    {
        lock (m_gate)
        {
            if (m_head == m_tail)
            {
                value = default!;
                return false;
            }

            value = m_buffer[m_head];
            m_buffer[m_head] = default!;
            m_head = (m_head + 1) % m_buffer.Length;
            return true;
        }
    }

    private int CountNoLock()
    {
        if (m_tail >= m_head)
        {
            return m_tail - m_head;
        }

        return m_buffer.Length - m_head + m_tail;
    }

    private void GrowNoLock()
    {
        var oldBuffer = m_buffer;
        var newBuffer = new T[oldBuffer.Length * 2];
        var count = CountNoLock();
        for (var i = 0; i < count; i++)
        {
            newBuffer[i] = oldBuffer[(m_head + i) % oldBuffer.Length];
        }

        m_buffer = newBuffer;
        m_head = 0;
        m_tail = count;
    }
}
