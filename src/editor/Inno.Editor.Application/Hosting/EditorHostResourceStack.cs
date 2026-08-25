using System;
using System.Collections.Generic;

namespace Inno.Editor.Application;

/// <summary>
/// Owns staged editor host resources and releases every acquired stage in reverse order.
/// </summary>
internal sealed class EditorHostResourceStack(Action<Exception> reportCleanupFailure) : IDisposable
{
    private readonly List<Action> m_cleanup = [];
    private bool m_disposed;

    internal T Acquire<T>(Func<T> factory, Action<T> cleanup)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(cleanup);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        T resource = factory();
        m_cleanup.Add(() => cleanup(resource));
        return resource;
    }

    internal void Register(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_cleanup.Add(cleanup);
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        for (int i = m_cleanup.Count - 1; i >= 0; i--)
        {
            try
            {
                m_cleanup[i]();
            }
            catch (Exception exception)
            {
                try
                {
                    reportCleanupFailure(exception);
                }
                catch
                {
                    // Cleanup diagnostics must never replace the original startup failure.
                }
            }
        }
        m_cleanup.Clear();
    }
}
