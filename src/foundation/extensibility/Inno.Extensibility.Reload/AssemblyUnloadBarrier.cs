using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Extensibility.Reload;

/// <summary>
/// Prevents a generation transition from completing until every retired collectible assembly is reclaimed.
/// </summary>
public sealed class AssemblyUnloadBarrier
{
    private readonly object m_sync = new();
    private readonly IAssemblyUnloadProbe[] m_probes;
    private readonly long m_collectionIntervalMilliseconds;
    private readonly long m_retentionTimeoutMilliseconds;
    private readonly long m_startedTimestamp;
    private long m_nextCollectionTimestamp;
    private int m_collectionAttempts;
    private AssemblyUnloadException? m_failure;
    private AssemblyUnloadBarrierState m_state;

    /// <summary>
    /// Creates a barrier over a complete retirement set.
    /// </summary>
    /// <param name="probes">
    /// Weak unload probes for every retired collectible generation in the transaction.
    /// </param>
    /// <param name="options">
    /// Optional collection cadence and retention threshold.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="probes"/> or one of its values is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an option duration is negative or the retention threshold is zero.
    /// </exception>
    public AssemblyUnloadBarrier(
        IEnumerable<IAssemblyUnloadProbe> probes,
        AssemblyUnloadBarrierOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(probes);
        m_probes = probes.ToArray();
        if (m_probes.Any(static probe => probe is null))
            throw new ArgumentNullException(nameof(probes), "Unload probe collections cannot contain null values.");
        options ??= new AssemblyUnloadBarrierOptions();
        if (options.collectionInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "The collection interval cannot be negative.");
        if (options.retentionTimeout <= TimeSpan.Zero && options.retentionTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), "The retention timeout must be positive or infinite.");
        m_collectionIntervalMilliseconds = checked((long)options.collectionInterval.TotalMilliseconds);
        m_retentionTimeoutMilliseconds = options.retentionTimeout == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : checked((long)options.retentionTimeout.TotalMilliseconds);
        m_startedTimestamp = Environment.TickCount64;
        m_nextCollectionTimestamp = m_startedTimestamp;
        m_state = m_probes.All(static probe => probe.isCompleted)
            ? AssemblyUnloadBarrierState.Completed
            : AssemblyUnloadBarrierState.AwaitingCollection;
    }

    /// <summary>
    /// Gets the current barrier state.
    /// </summary>
    public AssemblyUnloadBarrierState state
    {
        get
        {
            lock (m_sync)
                return m_state;
        }
    }

    /// <summary>
    /// Gets the number of full collection cycles performed by this barrier.
    /// </summary>
    public int collectionAttempts
    {
        get
        {
            lock (m_sync)
                return m_collectionAttempts;
        }
    }

    /// <summary>
    /// Gets the terminal failure after the barrier enters <see cref="AssemblyUnloadBarrierState.Faulted"/>.
    /// </summary>
    public AssemblyUnloadException? failure
    {
        get
        {
            lock (m_sync)
                return m_failure;
        }
    }

    /// <summary>
    /// Performs a due full collection cycle and reevaluates every retired generation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when every probe has completed; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="AssemblyUnloadException">
    /// Thrown on every call after the retention threshold is reached while a generation remains reachable.
    /// </exception>
    public bool Advance()
    {
        long now;
        bool collect;
        lock (m_sync)
        {
            if (m_state == AssemblyUnloadBarrierState.Completed)
                return true;
            if (m_failure is not null)
                throw m_failure;
            now = Environment.TickCount64;
            collect = now >= m_nextCollectionTimestamp;
            if (collect)
            {
                m_collectionAttempts++;
                m_nextCollectionTimestamp = now + m_collectionIntervalMilliseconds;
            }
        }

        if (collect)
            ForceFullCollection();

        lock (m_sync)
        {
            if (m_probes.All(static probe => probe.isCompleted))
            {
                m_state = AssemblyUnloadBarrierState.Completed;
                return true;
            }
            TimeSpan elapsed = TimeSpan.FromMilliseconds(Math.Max(0, now - m_startedTimestamp));
            if (elapsed.TotalMilliseconds < m_retentionTimeoutMilliseconds)
                return false;
            string[] retained = m_probes
                .Where(static probe => !probe.isCompleted)
                .Select(static probe => probe.description)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            m_failure = new AssemblyUnloadException(retained, elapsed, m_collectionAttempts);
            m_state = AssemblyUnloadBarrierState.Faulted;
            throw m_failure;
        }
    }

    /// <summary>
    /// Blocks the caller until all retired generations are reclaimed, cancellation is requested, or the barrier faults.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that can cancel the wait without changing the barrier state.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled before completion.
    /// </exception>
    /// <exception cref="AssemblyUnloadException">
    /// Thrown when the retention threshold is reached.
    /// </exception>
    public void Wait(CancellationToken cancellationToken = default)
    {
        while (!Advance())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int delay = (int)Math.Clamp(m_collectionIntervalMilliseconds, 1, 50);
            cancellationToken.WaitHandle.WaitOne(delay);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
