using System;
using System.Collections.Generic;
using Inno.Core.Execution;

namespace Inno.Rendering.Runtime;

internal sealed class RenderRetirementQueue : IDisposable
{
    private readonly Queue<Action> m_steps = [];
    private readonly List<Exception> m_failures = [];
    private bool m_stopping;

    internal void Add(Action retire)
    {
        if (m_stopping)
            throw new InvalidOperationException("Rendering retirement no longer accepts allocations.");
        m_steps.Enqueue(retire);
    }

    /// <summary>
    /// Drains the captured resource boundary without advancing past pending retirement.
    /// </summary>
    public void Dispose()
    {
        m_stopping = true;
        while (m_steps.TryPeek(out Action? step))
        {
            try { step(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_failures.Add(exception); }
            m_steps.Dequeue();
        }
        if (m_failures.Count > 0)
        {
            var failure = new AggregateException("Rendering retirement failed after all quiescent owners were attempted.", m_failures);
            m_failures.Clear();
            throw failure;
        }
    }
}
