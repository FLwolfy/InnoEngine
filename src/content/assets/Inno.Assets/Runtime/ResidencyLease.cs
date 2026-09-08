using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Assets;

internal sealed class ResidencyLease<TValue> : IDisposable where TValue : class
{
    private readonly object m_sync = new();
    private TValue? m_value;
    private Action? m_release;
    private List<Exception>? m_completedFailures;
    private bool m_releasing;

    internal ResidencyLease(TValue value, Action release)
    {
        m_value = value ?? throw new ArgumentNullException(nameof(value));
        m_release = release ?? throw new ArgumentNullException(nameof(release));
    }

    internal TValue value
    {
        get
        {
            lock (m_sync)
                return m_value ?? throw new ObjectDisposedException("Residency lease");
        }
    }

    /// <summary>
    /// Serializes release attempts while retaining the callback and value until retirement has a terminal outcome.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// The provider is still draining or another caller is currently advancing this lease's release.
    /// </exception>
    public void Dispose()
    {
        Action release;
        lock (m_sync)
        {
            if (m_release is null)
                return;
            if (m_releasing)
                throw new RetirementPendingException("Residency release is already being advanced by another invocation.");
            m_releasing = true;
            release = m_release;
        }
        try { release(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            m_completedFailures ??= [];
            RetirementPendingException.CollectCompletedFailures(pendingRetirement, m_completedFailures);
            lock (m_sync)
                m_releasing = false;
            throw;
        }
        catch (Exception exception)
        {
            Finish();
            if (m_completedFailures is { Count: > 0 })
            {
                RetirementPendingException.CollectCompletedFailures(exception, m_completedFailures);
                throw TakeCompletedFailure();
            }
            throw;
        }
        Finish();
        if (m_completedFailures is { Count: > 0 })
            throw TakeCompletedFailure();
    }

    private void Finish()
    {
        lock (m_sync)
        {
            m_release = null;
            m_value = null;
            m_releasing = false;
        }
    }

    private AggregateException TakeCompletedFailure()
    {
        var failure = new AggregateException("Residency release completed with earlier cleanup failures.", m_completedFailures!);
        m_completedFailures = null;
        return failure;
    }
}
