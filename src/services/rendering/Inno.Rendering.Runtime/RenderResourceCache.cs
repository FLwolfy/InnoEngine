using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Execution;

namespace Inno.Rendering.Runtime;

internal sealed class RenderResourceCache<TKey, TEntry>(Action<TEntry> release, int capacity)
    : IEnumerable<KeyValuePair<TKey, TEntry>>, IDisposable where TKey : notnull where TEntry : class
{
    private readonly Dictionary<TKey, TEntry> m_entries = [];
    private readonly Queue<TEntry> m_retiring = [];
    private RetirementBarrier? m_barrier;
    private bool m_stopping;
    private long m_rejected;
    private int m_peak;
    private readonly List<Exception> m_failures = [];

    internal IEnumerable<TEntry> Values => m_entries.Values;
    internal int Count => m_entries.Count;
    internal int pendingCount => m_retiring.Count;
    internal int peakCount => m_peak;
    internal long rejectedCount => m_rejected;

    internal bool TryGetValue(TKey key, [NotNullWhen(true)] out TEntry? value) => m_entries.TryGetValue(key, out value);

    internal void RequireCapacity(TKey key)
    {
        ObjectDisposedException.ThrowIf(m_stopping, this);
        Drain();
        if (!m_entries.ContainsKey(key) && m_entries.Count >= capacity)
        {
            m_rejected++;
            throw new InvalidOperationException($"Rendering resource capacity {capacity} has been reached.");
        }
    }

    internal void Replace(TKey key, TEntry candidate)
    {
        if (m_entries.TryGetValue(key, out TEntry? previous))
            m_retiring.Enqueue(previous);
        m_entries[key] = candidate;
        m_peak = Math.Max(m_peak, m_entries.Count);
    }

    internal void Release(TKey key)
    {
        if (m_entries.Remove(key, out TEntry? entry))
            m_retiring.Enqueue(entry);
        Drain();
    }

    internal void Sweep(Func<TEntry, bool> unused)
    {
        Drain();
        foreach (TKey key in m_entries.Where(pair => unused(pair.Value)).Select(pair => pair.Key).ToArray())
            Release(key);
    }

    internal void Drain()
    {
        List<Exception> failures = m_failures;
        while (m_retiring.TryPeek(out TEntry? entry))
        {
            m_barrier ??= new RetirementBarrier("Rendering resource replacement");
            try
            {
                if (!m_barrier.TryComplete(() => release(entry)))
                    throw new RetirementPendingException("A retired GPU resource still owns unfinished work.");
            }
            catch (Exception pending) when (RetirementPendingException.Find(pending) is not null)
            {
                if (failures.Count != 0)
                    throw new AggregateException("GPU retirement is pending after completed failures.", [.. failures, pending]);
                throw;
            }
            catch (Exception failure) { failures.Add(failure); }
            m_retiring.Dequeue();
            m_barrier = null;
        }
        if (failures.Count != 0)
        {
            var failure = new AggregateException("GPU resources retired with completed failures.", failures);
            failures.Clear();
            throw failure;
        }
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (!m_stopping)
        {
            m_stopping = true;
            foreach (TEntry entry in m_entries.Values)
                m_retiring.Enqueue(entry);
            m_entries.Clear();
        }
        Drain();
    }

    /// <summary>
    /// Gets an enumerator required by the implemented contract.
    /// </summary>
    /// <returns>
    /// The value produced by this implementation of the contract.
    /// </returns>
    public IEnumerator<KeyValuePair<TKey, TEntry>> GetEnumerator() => m_entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
