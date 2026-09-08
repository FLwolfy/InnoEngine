using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Editor.Application;

/// <summary>
/// Owns staged editor host resources and releases every acquired stage in reverse order.
/// </summary>
/// <param name="reportCleanupFailure">
/// The report cleanup failure used to initialize this instance.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
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

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        List<Exception> failures = [];
        for (int i = m_cleanup.Count - 1; i >= 0; i--)
        {
            Action cleanup = m_cleanup[i];
            try
            {
                cleanup();
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                failures.Add(exception);
                try
                {
                    reportCleanupFailure(exception);
                }
                catch
                {
                    // Cleanup diagnostics must never replace the original startup failure.
                }
            }
            m_cleanup.RemoveAt(i);
        }
        m_disposed = true;
        m_cleanup.Clear();
        if (failures.Count > 0)
            throw new AggregateException("Editor resources could not all retire cleanly.", failures);
    }
}
