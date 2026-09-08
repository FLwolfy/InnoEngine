using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Core.Jobs;

internal sealed class MainThreadWorkQueue(int capacity, int drainBudget)
{
    private readonly object m_sync = new();
    private readonly Queue<Action> m_actions = [];
    private bool m_closed;
    private bool m_executing;
    private readonly List<Exception> m_failures = [];
    private Action? m_pending;
    private RetirementBarrier? m_barrier;
    private int m_peak;
    private long m_rejected;
    private long m_canceled;

    internal JobSchedulerStatistics statistics
    {
        get
        {
            lock (m_sync) return new JobSchedulerStatistics
            {
                mainThreadPending = m_actions.Count + (m_pending is null ? 0 : 1),
                mainThreadPeak = m_peak, mainThreadRejected = m_rejected, mainThreadCanceled = m_canceled
            };
        }
    }

    internal bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (m_sync)
        {
            if (m_closed || m_actions.Count >= capacity)
            {
                m_rejected++;
                return false;
            }
            m_actions.Enqueue(action);
            m_peak = Math.Max(m_peak, m_actions.Count + (m_pending is null ? 0 : 1));
            return true;
        }
    }

    internal void Drain()
    {
        try { CompletePending(); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception failure) { m_failures.Add(failure); }
        int count;
        lock (m_sync)
            count = Math.Min(drainBudget, m_actions.Count);
        for (int index = 0; index < count; index++)
        {
            Action action;
            lock (m_sync)
            {
                if (!m_actions.TryDequeue(out action!))
                    break;
            }
            lock (m_sync) m_pending = action;
            try { CompletePending(); }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null)
            {
                if (m_failures.Count > 0) throw new AggregateException("Callbacks remain pending after completed failures.", [.. m_failures, pending]);
                throw;
            }
            catch (Exception exception) { m_failures.Add(exception); }
        }
        ReportFailures();
    }

    internal void Close()
    {
        lock (m_sync)
        {
            m_closed = true;
            m_canceled += m_actions.Count;
            m_actions.Clear();
        }
        try { CompletePending(); }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception failure) { m_failures.Add(failure); }
        ReportFailures();
    }

    private void ReportFailures()
    {
        if (m_failures.Count == 0) return;
        var failure = new AggregateException("Main-thread callbacks completed with failures.", m_failures);
        m_failures.Clear();
        throw failure;
    }

    private void CompletePending()
    {
        if (m_executing) throw new RetirementPendingException("A main-thread callback cannot retire itself reentrantly.");
        Action? action;
        lock (m_sync) action = m_pending;
        if (action is null) return;
        m_barrier ??= new RetirementBarrier("Job main-thread callback");
        m_executing = true;
        try
        {
            if (!m_barrier.TryComplete(action))
                throw new RetirementPendingException("An owner-thread job callback has unfinished retirement.");
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch { ClearPending(); throw; }
        finally { m_executing = false; }
        ClearPending();
    }

    private void ClearPending()
    {
        lock (m_sync) m_pending = null;
        m_barrier = null;
    }
}
