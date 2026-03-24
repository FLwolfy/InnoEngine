namespace Inno.Graphics;

/// <summary>
/// Represents a thread-safe CPU-side synchronization primitive for graphics workflows.
/// </summary>
public sealed class GraphicsFence : IDisposable
{
    private readonly ManualResetEventSlim m_signal = new(false);
    private readonly object m_lock = new();
    private ulong m_value;
    private bool m_disposed;

    public ulong value
    {
        get
        {
            lock (m_lock)
            {
                return m_value;
            }
        }
    }

    public bool isSignaled => m_signal.IsSet;

    public void Signal(ulong value)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_lock)
        {
            if (value > m_value)
            {
                m_value = value;
            }
        }

        m_signal.Set();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_signal.Reset();
    }

    public bool Wait(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return m_signal.Wait(timeout);
    }

    public void Wait()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_signal.Wait();
    }

    public async ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return await Task.Run(() => m_signal.Wait(timeout, cancellationToken), cancellationToken);
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_signal.Dispose();
        m_disposed = true;
    }
}
