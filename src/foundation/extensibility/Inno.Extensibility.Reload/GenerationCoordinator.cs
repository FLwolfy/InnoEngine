using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

using Inno.Core.Execution;
namespace Inno.Extensibility.Reload;

/// <summary>
/// Owns generation admission, atomic domain changes, rollback, and non-bypassable weak unload verification.
/// </summary>
public sealed class GenerationCoordinator
{
    private readonly object m_sync = new();
    private readonly List<IAssemblyUnloadProbe> m_pending = [];
    private AssemblyUnloadBarrierOptions m_options = new();
    private AssemblyUnloadBarrier? m_barrier;
    private GenerationState m_state;
    private Exception? m_failure;
    private int m_readers;
    private int m_publicationThread;
    private bool m_advancing;
    private object? m_retainedTransaction;
    private RetirementPendingException? m_retirementFailure;

    /// <summary>
    /// Gets the gate state shared by reload, Play, Build and Export owners.
    /// </summary>
    public GenerationState state { get { lock (m_sync) return m_state; } }

    /// <summary>
    /// Gets the terminal failure, retained until the entire host is discarded.
    /// </summary>
    public Exception? failure { get { lock (m_sync) return m_failure; } }

    /// <summary>
    /// Configures collection cadence before beginning a generation or retirement.
    /// </summary>
    /// <param name="options">
    /// The collection and retention policy, copied by the coordinator.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The gate is not ready.
    /// </exception>
    public void Configure(AssemblyUnloadBarrierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = new AssemblyUnloadBarrier([], options);
        EnsureReady("configure unload verification");
        lock (m_sync)
            m_options = new AssemblyUnloadBarrierOptions
            {
                collectionInterval = options.collectionInterval,
                retentionTimeout = options.retentionTimeout
            };
    }

    /// <summary>
    /// Rejects work while a candidate, retained context or terminal failure is pending.
    /// </summary>
    /// <param name="operation">
    /// The caller's operation name included in rejection diagnostics.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Admission is blocked or the host requires restart.
    /// </exception>
    public void EnsureReady(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (state == GenerationState.AwaitingCollection)
            _ = Advance();
        lock (m_sync)
        {
            if (m_failure is not null)
                throw new InvalidOperationException($"Cannot {operation}: the generation owner is Faulted and must restart.", m_failure);
            if (m_state != GenerationState.Ready)
                throw new InvalidOperationException($"Cannot {operation}: generation state is {m_state}.");
            if (m_readers > 0)
                throw new InvalidOperationException($"Cannot {operation}: an owner operation holds a generation read lease.");
        }
    }

    /// <summary>
    /// Pins generation admission for a Build or Export operation, including asynchronous snapshot consumers.
    /// </summary>
    /// <param name="operation">
    /// The diagnostic operation name.
    /// </param>
    /// <returns>
    /// A lease that must be disposed after every snapshot and asynchronous consumer has retired.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Admission is blocked by another operation or a retirement.
    /// </exception>
    public IDisposable AcquireRead(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (state == GenerationState.AwaitingCollection)
            _ = Advance();
        lock (m_sync)
        {
            if (m_state != GenerationState.Ready || m_failure is not null)
                throw new InvalidOperationException($"Cannot {operation}: generation state is {m_state}.", m_failure);
            m_readers++;
            return new ReadLease(this);
        }
    }

    /// <summary>
    /// Keeps one synchronous operation on the published generation and defers automatic catalog replacement.
    /// </summary>
    /// <param name="operation">
    /// The operation name included in admission failures.
    /// </param>
    /// <returns>
    /// A scope for the entire operation, or a borrowed scope inside publication on its own control thread.
    /// </returns>
    /// <remarks>
    /// Published data remains readable while old contexts await collection. This scope does not authorize Play,
    /// Build, Export or a new generation transaction; those owners must still use their strict admission boundary.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Another thread is publishing or the owner is Faulted.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// Retirement has failed without releasing its dependent owners.
    /// </exception>
    public IDisposable AcquireOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        EnsureRetirementSafe();
        lock (m_sync)
        {
            if (m_failure is not null)
                throw new InvalidOperationException($"Cannot {operation}: the generation owner is Faulted.", m_failure);
            if (m_state == GenerationState.Transitioning)
            {
                if (m_publicationThread != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException($"Cannot {operation}: another thread is publishing a generation.");
                return new ReadLease(null);
            }
            m_readers++;
            return new ReadLease(this);
        }
    }

    /// <summary>
    /// Tries to reserve exclusive owner-controlled publication without disturbing active read leases.
    /// </summary>
    /// <param name="operation">
    /// The operation name used when a terminal failure rejects admission.
    /// </param>
    /// <param name="reservation">
    /// Receives the exclusive reservation, or null when the caller must defer mutation.
    /// </param>
    /// <returns>
    /// True only when no reader, generation change or retirement is active.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The generation owner is Faulted and requires a host restart.
    /// </exception>
    public bool TryAcquireChange(string operation, out IDisposable? reservation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        lock (m_sync)
        {
            if (m_failure is not null)
                throw new InvalidOperationException($"Cannot {operation}: restart the Faulted generation owner.", m_failure);
            reservation = null;
            if (m_state != GenerationState.Ready || m_readers != 0)
                return false;
            m_state = GenerationState.Transitioning;
            m_publicationThread = Environment.CurrentManagedThreadId;
            reservation = new ChangeReservation(this);
            return true;
        }
    }

    /// <summary>
    /// Registers every retired or discarded context without dropping already pending monitors.
    /// </summary>
    /// <param name="probe">
    /// A stable engine-owned weak monitor that must not retain the collectible context.
    /// </param>
    public void TrackRetirement(IAssemblyUnloadProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        lock (m_sync)
        {
            if (probe.isCompleted || m_pending.Contains(probe))
                return;
            m_pending.Add(probe);
            if (m_state == GenerationState.Ready)
                m_state = GenerationState.AwaitingCollection;
        }
    }

    /// <summary>
    /// Activates one publication and its captured domain changes, preserving all rollback and cleanup failures.
    /// </summary>
    /// <typeparam name="TProbe">
    /// The publication's weak unload monitor.
    /// </typeparam>
    /// <param name="operation">
    /// A stable diagnostic name for the generation operation.
    /// </param>
    /// <param name="publication">
    /// The prepared candidate publication.
    /// </param>
    /// <param name="changes">
    /// Captured domain changes in deterministic dependency order.
    /// </param>
    /// <returns>
    /// The retirement monitor; admission remains blocked until all monitors pass verification.
    /// </returns>
    /// <exception cref="AggregateException">
    /// An irreversible cleanup or rollback stage failed and the gate is Faulted.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Another generation or retirement is pending.
    /// </exception>
    public TProbe Execute<TProbe>(string operation, IGenerationPublication<TProbe> publication,
        IReadOnlyList<IGenerationChange> changes) where TProbe : IAssemblyUnloadProbe
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(changes);
        IGenerationChange[] snapshot = changes.ToArray();
        if (snapshot.Any(static change => change is null))
            throw new ArgumentException("Generation changes cannot contain null entries.", nameof(changes));
        EnsureReady(operation);
        lock (m_sync)
        {
            if (m_state != GenerationState.Ready || m_readers != 0 || m_failure is not null)
                throw new InvalidOperationException($"Cannot {operation}: generation admission changed while preparing the transaction.");
            m_state = GenerationState.Transitioning;
            m_publicationThread = Environment.CurrentManagedThreadId;
        }
        int preparedCount = 0;
        bool committed = false;
        bool retirementBlocked = false;
        try
        {
            while (preparedCount < snapshot.Length)
                snapshot[preparedCount++].PrepareForActivation();
            publication.Activate();
            foreach (IGenerationChange change in snapshot)
                change.Apply();
            committed = true;
            TProbe probe = publication.Complete();
            TrackRetirement(probe);
            List<Exception> cleanup = [];
            foreach (IGenerationChange change in snapshot)
                Attempt(change.Complete, cleanup);
            if (cleanup.Count > 0)
                throw new AggregateException($"Generation '{operation}' committed but retirement cleanup failed.", cleanup);
            return probe;
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            retirementBlocked = true;
            RetainFailedRetirement(failure, (publication, snapshot));
            throw;
        }
        catch (Exception error)
        {
            if (committed)
            {
                Fault(error);
                throw;
            }
            List<Exception> rollback = [];
            try
            {
                for (int index = preparedCount - 1; index >= 0; index--)
                    Attempt(snapshot[index].RollbackStructure, rollback);
                Attempt(publication.Rollback, rollback);
                for (int index = preparedCount - 1; index >= 0; index--)
                    Attempt(snapshot[index].RestorePreviousState, rollback);
            }
            catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
            {
                retirementBlocked = true;
                var combined = new AggregateException($"Generation '{operation}' failed and rollback remains pending.", error, failure);
                RetainFailedRetirement(combined, (publication, snapshot));
                throw combined;
            }
            if (rollback.Count > 0)
            {
                var failure = new AggregateException($"Generation '{operation}' failed and rollback was incomplete.", [error, .. rollback]);
                Fault(failure);
                throw failure;
            }
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        finally
        {
            if (!retirementBlocked)
                Array.Clear(snapshot);
            lock (m_sync)
            {
                m_publicationThread = 0;
                if (m_state != GenerationState.Faulted)
                    m_state = m_pending.Count == 0 ? GenerationState.Ready : GenerationState.AwaitingCollection;
            }
        }
    }

    /// <summary>
    /// Runs a due Full GC/finalizer/Full GC cycle only after candidate stack frames have unwound.
    /// </summary>
    /// <returns>
    /// True only when every tracked context is unreachable and admission is ready.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The gate is terminally Faulted.
    /// </exception>
    /// <exception cref="AssemblyUnloadException">
    /// A retained context exceeded the configured deadline.
    /// </exception>
    public bool Advance()
    {
        AssemblyUnloadBarrier barrier;
        lock (m_sync)
        {
            if (m_failure is not null)
                throw new InvalidOperationException("Generation retirement failed; restart the host.", m_failure);
            if (m_state == GenerationState.Transitioning || m_advancing)
                return false;
            if (m_pending.Count == 0)
            {
                m_state = GenerationState.Ready;
                return true;
            }
            barrier = m_barrier ??= new AssemblyUnloadBarrier(m_pending.ToArray(), m_options);
            m_advancing = true;
        }
        try
        {
            // Finalizers may call engine boundaries; never hold the admission lock during collection.
            bool completed = barrier.Advance();
            lock (m_sync)
            {
                if (completed)
                {
                    m_pending.RemoveAll(static probe => probe.isCompleted);
                    m_barrier = null;
                }
                if (m_pending.Count == 0)
                    m_state = GenerationState.Ready;
                return m_pending.Count == 0;
            }
        }
        catch (Exception exception)
        {
            Fault(exception);
            throw;
        }
        finally
        {
            lock (m_sync) m_advancing = false;
        }
    }

    /// <summary>
    /// Waits for all retirements while preserving pending monitors on cancellation or failure.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation for the wait, not permission to resume generation changes.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// The caller canceled before retirement completed.
    /// </exception>
    public void Wait(CancellationToken cancellationToken = default)
    {
        while (!Advance())
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.WaitHandle.WaitOne(10);
        }
    }

    /// <summary>
    /// Permanently closes admission when an owner cannot safely retire or restore generation state.
    /// </summary>
    /// <param name="exception">
    /// The cleanup or rollback failure retained for diagnostics until the host restarts.
    /// </param>
    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (m_sync)
        {
            m_failure ??= exception;
            m_retirementFailure ??= RetirementPendingException.Find(exception);
            m_state = GenerationState.Faulted;
        }
    }

    /// <summary>
    /// Rejects dependency destruction after a generation owner reported unfinished retirement.
    /// </summary>
    /// <remarks>
    /// Ordinary terminal cleanup failures do not prevent remaining cleanup. Unfinished retirement remains
    /// blocking even if an earlier ordinary error is the primary diagnostic failure.
    /// </remarks>
    /// <exception cref="RetirementPendingException">
    /// A failed generation still owns live work; its dependencies must remain owned until host restart.
    /// </exception>
    public void EnsureRetirementSafe()
    {
        lock (m_sync)
        {
            if (m_retirementFailure is not null)
                throw m_retirementFailure;
        }
    }

    private static void Attempt(Action operation, ICollection<Exception> failures)
    {
        try { operation(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            if (failures.Count > 0)
                throw new AggregateException("Generation retirement remains pending after earlier cleanup failures.", [.. failures, pendingRetirement]);
            throw;
        }
        catch (Exception exception) { failures.Add(exception); }
    }

    private void RetainFailedRetirement(Exception failure, object transaction)
    {
        lock (m_sync)
            m_retainedTransaction = transaction;
        Fault(failure);
        GC.KeepAlive(m_retainedTransaction);
    }

    private sealed class ChangeReservation(GenerationCoordinator owner) : IDisposable
    {
        private GenerationCoordinator? m_owner = owner;

        /// <summary>
        /// Ends exclusive publication while preserving pending retirement and terminal failure state.
        /// </summary>
        public void Dispose()
        {
            GenerationCoordinator? current = Interlocked.Exchange(ref m_owner, null);
            if (current is null)
                return;
            lock (current.m_sync)
            {
                current.m_publicationThread = 0;
                if (current.m_state != GenerationState.Faulted)
                    current.m_state = current.m_pending.Count == 0 ? GenerationState.Ready : GenerationState.AwaitingCollection;
            }
        }
    }

    private sealed class ReadLease(GenerationCoordinator? owner) : IDisposable
    {
        private GenerationCoordinator? m_owner = owner;
        /// <summary>
        /// Releases this owner's registrations and resources exactly once.
        /// </summary>
        public void Dispose()
        {
            GenerationCoordinator? current = Interlocked.Exchange(ref m_owner, null);
            if (current is null)
                return;
            lock (current.m_sync)
                current.m_readers--;
        }
    }
}
