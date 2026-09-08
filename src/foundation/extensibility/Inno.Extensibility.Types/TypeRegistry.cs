using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Inno.Core.Execution;

namespace Inno.Extensibility.Types;

/// <summary>
/// Builds immutable extension registries from versioned type-cache snapshots.
/// </summary>
/// <typeparam name="TSnapshot">
/// The immutable registry snapshot type.
/// </typeparam>
public abstract class TypeRegistry<TSnapshot> : IDisposable
    where TSnapshot : class
{
    private readonly object m_sync = new();
    private readonly TypeCatalog m_types;
    private readonly RegistryAdapter m_registryAdapter;
    private readonly TypeRegistryRegistration m_registration;

    private TSnapshot? m_current;
    private long m_typeCacheVersion = -1;
    private bool m_activationInProgress;
    private bool m_disposed;
    private List<object>? m_candidateResources;
    private readonly TimeSpan m_retirementTimeout;
    private Exception? m_retirementFailure;
    private object? m_retainedRetirement;

    /// <summary>
    /// Creates and registers a refreshable type registry.
    /// </summary>
    /// <param name="types">
    /// The type catalog that coordinates this registry's candidate generations.
    /// </param>
    /// <param name="retirementTimeout">
    /// The positive owner-thread drain deadline; omitted values use thirty seconds.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="types"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The retirement timeout is not positive.
    /// </exception>
    protected TypeRegistry(TypeCatalog types, TimeSpan? retirementTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_retirementTimeout = retirementTimeout ?? TimeSpan.FromSeconds(30);
        if (m_retirementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retirementTimeout));
        m_types = types;
        m_registryAdapter = new RegistryAdapter(this);
        m_registration = types.Register(m_registryAdapter);
    }

    /// <summary>
    /// Gets whether the registry has built its first snapshot.
    /// </summary>
    public bool isInitialized
    {
        get
        {
            lock (m_sync)
                return m_current is not null;
        }
    }

    /// <summary>
    /// Refreshes this registry from the currently active type snapshot.
    /// </summary>
    /// <remarks>
    /// Each refresh iteration is a complete transaction. If the active type snapshot changes during
    /// activation, convergence runs as a separate transaction after the completed transaction returns.
    /// </remarks>
    public void Refresh()
    {
        EnsureRetirementSafe();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        while (true)
        {
            ITypeRegistryTransaction transaction = Prepare(
                m_types.current,
                allowDisposed: false);
            try
            {
                transaction.Activate();
                transaction.Complete();
            }
            catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
            {
                throw;
            }
            catch (Exception activationFailure)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Registry refresh and rollback failed.", activationFailure, rollbackFailure);
                }
                throw;
            }

            TypeCacheSnapshot latest = m_types.current;
            lock (m_sync)
            {
                if (m_activationInProgress ||
                    m_current is not null && m_typeCacheVersion == latest.version)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Releases the active snapshot while keeping the registry reusable.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Generation work could not retire safely. The snapshot remains owned and this registry cannot be reused.
    /// </exception>
    public void Clear()
    {
        EnsureRetirementSafe();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        TSnapshot? snapshot;
        lock (m_sync)
        {
            if (m_activationInProgress)
            {
                throw new InvalidOperationException(
                    "A registry snapshot cannot be cleared while a refresh transaction is active.");
            }
            snapshot = m_current;
        }

        try
        {
            if (snapshot is not null)
                ReleaseSnapshot(snapshot);
        }
        finally
        {
            if (m_retirementFailure is null)
            {
                lock (m_sync)
                {
                    m_current = null;
                    m_typeCacheVersion = -1;
                }
            }
        }
    }

    /// <summary>
    /// Releases the current registry snapshot.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Live generation work remains; repeated disposal cannot bypass its retained ownership barrier.
    /// </exception>
    public void Dispose()
    {
        EnsureRetirementSafe();
        TSnapshot? snapshot;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            snapshot = m_activationInProgress ? null : m_current;
        }

        try
        {
            if (snapshot is not null)
                ReleaseSnapshot(snapshot);
        }
        finally
        {
            if (m_retirementFailure is null)
            {
                m_registration.Dispose();
                lock (m_sync)
                {
                    m_current = null;
                    m_typeCacheVersion = -1;
                }
            }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the current snapshot, refreshing it when the type catalog changed.
    /// </summary>
    protected TSnapshot current
    {
        get
        {
            EnsureRetirementSafe();
            TypeCacheSnapshot types = m_types.current;
            lock (m_sync)
            {
                if (m_current is not null && m_typeCacheVersion == types.version)
                    return m_current;
            }

            Refresh();
            lock (m_sync)
                return m_current ?? throw new InvalidOperationException("Registry refresh produced no snapshot.");
        }
    }

    /// <summary>
    /// Builds a complete candidate registry without changing the active snapshot.
    /// </summary>
    /// <param name="types">
    /// The candidate type-cache snapshot.
    /// </param>
    /// <returns>
    /// The complete immutable registry snapshot.
    /// </returns>
    protected abstract TSnapshot Build(TypeCacheSnapshot types);

    /// <summary>
    /// Reversibly activates a complete candidate while the previous snapshot remains available.
    /// </summary>
    /// <param name="previous">
    /// The previous active snapshot, or <see langword="null"/> during initial activation.
    /// </param>
    /// <param name="candidate">
    /// The candidate exposed through <see cref="current"/> during this callback.
    /// </param>
    /// <remarks>
    /// Implementations may perform fallible lifecycle work here. They must keep enough local state for
    /// <see cref="OnActivationRolledBack"/> to reverse every completed step.
    /// </remarks>
    protected virtual void OnActivating(TSnapshot? previous, TSnapshot candidate)
    {
    }

    /// <summary>
    /// Reverses lifecycle work performed while activating a candidate snapshot.
    /// </summary>
    /// <param name="previous">
    /// The restored active snapshot, or <see langword="null"/> when none existed.
    /// </param>
    /// <param name="candidate">
    /// The rejected candidate snapshot.
    /// </param>
    /// <remarks>
    /// Ordinary failures are reported through <see cref="OnCleanupFailed"/> and aggregated across registries.
    /// Unfinished retirement retains dependencies and stops further rollback until the host is restarted.
    /// </remarks>
    protected virtual void OnActivationRolledBack(TSnapshot? previous, TSnapshot candidate)
    {
    }

    /// <summary>
    /// Finalizes a successfully activated candidate after every coordinated registry has activated.
    /// </summary>
    /// <param name="previous">
    /// The previous snapshot that is about to be released, or <see langword="null"/>.
    /// </param>
    /// <param name="currentSnapshot">
    /// The committed active snapshot.
    /// </param>
    /// <remarks>
    /// This is a cleanup-only phase and must not perform fallible publication work. Exceptions are reported
    /// through <see cref="OnCleanupFailed"/> and cannot cause the completed activation to roll back.
    /// </remarks>
    protected virtual void OnActivationCompleted(TSnapshot? previous, TSnapshot currentSnapshot)
    {
    }

    /// <summary>
    /// Releases resources owned by a registry snapshot.
    /// </summary>
    /// <param name="snapshot">
    /// The snapshot that is no longer active.
    /// </param>
    protected virtual void DisposeSnapshot(TSnapshot snapshot)
    {
        if (snapshot is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Reports an exception raised while rolling back activation, completing activation, or
    /// releasing a snapshot.
    /// </summary>
    /// <param name="phase">
    /// The non-transactional phase that raised the exception.
    /// </param>
    /// <param name="exception">
    /// The cleanup exception.
    /// </param>
    /// <remarks>
    /// This callback is diagnostic only. Exceptions raised by an override are ignored so cleanup can continue.
    /// </remarks>
    protected virtual void OnCleanupFailed(string phase, Exception exception)
        => Trace.TraceError(
            "Type registry '{0}' failed during {1}: {2}",
            GetType().FullName,
            phase,
            exception);

    /// <summary>
    /// Faults the shared generation gate when a resource composed from this registry cannot retire safely.
    /// </summary>
    /// <param name="exception">
    /// The retirement or compensation failure; this notification never makes publication reversible.
    /// </param>
    protected void ReportRetirementFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ReportCleanupFailure("composed resource retirement", exception);
    }

    /// <summary>
    /// Creates a validated extension instance using a parameterless constructor.
    /// </summary>
    /// <typeparam name="TExtension">
    /// The required extension contract.
    /// </typeparam>
    /// <param name="type">
    /// The concrete implementation type.
    /// </param>
    /// <returns>
    /// A newly created extension, retained for rollback until the complete candidate snapshot takes ownership.
    /// </returns>
    protected TExtension CreateExtension<TExtension>(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsAbstract || !typeof(TExtension).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Extension '{type.FullName}' must be a non-abstract {typeof(TExtension).FullName}.");
        }

        try
        {
            return OwnCandidateExtension((TExtension)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException("Activator returned null.")));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Extension '{type.FullName}' requires a parameterless constructor.",
                exception);
        }
    }

    /// <summary>
    /// Adds a newly constructed extension to the current build's rollback ownership before further validation.
    /// </summary>
    /// <typeparam name="TExtension">
    /// The newly constructed extension contract.
    /// </typeparam>
    /// <param name="extension">
    /// A new instance, never an instance borrowed from the active snapshot.
    /// </param>
    /// <returns>
    /// The same instance. After a successful build its containing snapshot must own its retirement.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This method is called outside synchronous candidate construction.
    /// </exception>
    protected TExtension OwnCandidateExtension<TExtension>(TExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (m_candidateResources is null)
            throw new InvalidOperationException("Candidate resources may only be owned while building a registry snapshot.");
        if (extension is IDisposable)
            m_candidateResources.Add(extension);
        return extension;
    }

    /// <summary>
    /// Retires distinct disposable extensions in reverse order, draining pending work before lower dependencies.
    /// </summary>
    /// <param name="extensions">
    /// Instances whose ownership has ended; duplicate references are disposed once.
    /// </param>
    /// <exception cref="AggregateException">
    /// One or more instances failed retirement after all remaining instances were attempted.
    /// </exception>
    /// <exception cref="RetirementTimeoutException">
    /// An instance did not drain. The registry retains the batch and faults its shared generation gate.
    /// </exception>
    protected void DisposeExtensions(IEnumerable<object> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var resources = new LifetimeScope();
        foreach (IDisposable resource in extensions.Distinct(ReferenceEqualityComparer.Instance).OfType<IDisposable>())
            resources.Own(resource);
        RetireResource("extension batch", resources.Dispose);
    }

    /// <summary>
    /// Drains one retryable lifecycle operation at the control-thread safe point before releasing dependencies.
    /// </summary>
    /// <param name="owner">
    /// The resource or extension identifier included in deadline diagnostics.
    /// </param>
    /// <param name="retire">
    /// The retirement operation, including owner-thread completion pumping when required.
    /// </param>
    /// <exception cref="RetirementTimeoutException">
    /// The operation exceeded its deadline. Its closure stays owned and further retirement or refresh is blocked.
    /// </exception>
    protected void RetireResource(string owner, Action retire)
    {
        EnsureRetirementSafe();
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(retire);
        try
        {
            new RetirementBarrier($"{GetType().FullName}: {owner}", m_retirementTimeout).Wait(retire);
        }
        catch (Exception exception) when (RetirementPendingException.Find(exception) is not null)
        {
            RetainFailedRetirement(exception, retire, owner);
            throw;
        }
    }

    private ITypeRegistryTransaction Prepare(TypeCacheSnapshot types, bool allowDisposed)
    {
        EnsureRetirementSafe();
        lock (m_sync)
        {
            if (m_disposed)
            {
                if (allowDisposed)
                    return TypeRegistryNoopTransaction.instance;
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (m_activationInProgress)
                return TypeRegistryNoopTransaction.instance;
            if (m_current is not null && m_typeCacheVersion == types.version)
                return TypeRegistryNoopTransaction.instance;

            m_activationInProgress = true;
            m_candidateResources = [];
            try
            {
                TSnapshot candidate = Build(types);
                return new RegistryTransaction(
                    this,
                    candidate,
                    types.version,
                    m_current,
                    m_typeCacheVersion);
            }
            catch (Exception failure)
            {
                m_activationInProgress = false;
                try { DisposeExtensions(m_candidateResources); }
                catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
                catch (Exception cleanup)
                {
                    ReportCleanupFailure("candidate construction rollback", cleanup);
                    throw new AggregateException("Registry construction and candidate rollback failed.", failure, cleanup);
                }
                throw;
            }
            finally
            {
                m_candidateResources.Clear();
                m_candidateResources = null;
            }
        }
    }

    private void RollbackActivation(TSnapshot? previous, TSnapshot candidate)
    {
        try
        {
            OnActivationRolledBack(previous, candidate);
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            RetainFailedRetirement(failure, (previous, candidate), "activation rollback");
            throw;
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("activation rollback", exception);
            throw;
        }
    }

    private void ReleaseSnapshot(TSnapshot snapshot)
    {
        try
        {
            RetireResource("snapshot release", () => DisposeSnapshot(snapshot));
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("snapshot release", exception);
            throw;
        }
    }

    private void CompleteActivation(TSnapshot? previous, TSnapshot candidate)
    {
        try
        {
            OnActivationCompleted(previous, candidate);
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
        {
            RetainFailedRetirement(failure, (previous, candidate), "activation completion");
            throw;
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("activation completion", exception);
            throw;
        }
    }

    private void ReportCleanupFailure(string phase, Exception exception)
    {
        m_types.ReportGenerationFailure(exception);
        try
        {
            OnCleanupFailed(phase, exception);
        }
        catch
        {
        }
    }

    private void EnsureRetirementSafe()
    {
        if (m_retirementFailure is not null)
        {
            GC.KeepAlive(m_retainedRetirement);
            throw m_retirementFailure;
        }
        if (!m_disposed)
            m_types.EnsureRetirementSafe();
    }

    private void RetainFailedRetirement(Exception failure, object owner, string phase)
    {
        m_retirementFailure = failure;
        m_retainedRetirement = owner;
        ReportCleanupFailure(phase, failure);
    }

    private sealed class RegistryAdapter(TypeRegistry<TSnapshot> owner) : ITypeRegistry
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        /// <param name="types">
        /// The active type catalog generation used for extension resolution.
        /// </param>
        /// <returns>
        /// The validated itype registry transaction that represents the completed operation.
        /// </returns>
        public ITypeRegistryTransaction Prepare(TypeCacheSnapshot types)
            => owner.Prepare(types, allowDisposed: true);
    }

    private sealed class RegistryTransaction(
        TypeRegistry<TSnapshot> owner,
        TSnapshot candidate,
        long candidateVersion,
        TSnapshot? previous,
        long previousVersion) : ITypeRegistryTransaction
    {
        private bool m_activated;
        private bool m_activationStarted;
        private bool m_finished;

        /// <summary>
        /// Makes the prepared value active at the owning subsystem's safety point.
        /// </summary>
        public void Activate()
        {
            bool ownerWasDisposed;
            lock (owner.m_sync)
            {
                EnsureNotFinished();
                ownerWasDisposed = owner.m_disposed;
                if (!ownerWasDisposed)
                {
                    owner.m_current = candidate;
                    owner.m_typeCacheVersion = candidateVersion;
                    m_activated = true;
                    m_activationStarted = true;
                }
            }

            if (ownerWasDisposed)
            {
                Rollback();
                return;
            }

            try
            {
                owner.OnActivating(previous, candidate);
                lock (owner.m_sync)
                    ObjectDisposedException.ThrowIf(owner.m_disposed, owner);
            }
            catch (Exception failure) when (RetirementPendingException.Find(failure) is not null)
            {
                owner.RetainFailedRetirement(failure, this, "activation retirement");
                throw;
            }
            catch (Exception activationFailure)
            {
                try { Rollback(); }
                catch (Exception rollbackFailure)
                {
                    var combined = new AggregateException("Registry activation and rollback failed.", activationFailure, rollbackFailure);
                    if (RetirementPendingException.Find(combined) is not null)
                        owner.RetainFailedRetirement(combined, this, "activation rollback");
                    throw combined;
                }
                throw;
            }
        }

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete()
        {
            owner.EnsureRetirementSafe();
            TSnapshot? snapshotToRelease;
            lock (owner.m_sync)
            {
                if (m_finished)
                    return;
                if (!m_activated)
                    throw new InvalidOperationException("Registry transaction has not been activated.");
                m_finished = true;
                snapshotToRelease = previous;
            }

            List<Exception> failures = [];
            Attempt(() => owner.CompleteActivation(previous, candidate), failures);
            if (snapshotToRelease is not null)
                Attempt(() => owner.ReleaseSnapshot(snapshotToRelease), failures);
            bool releaseCandidate;
            lock (owner.m_sync)
            {
                releaseCandidate = owner.m_disposed;
                owner.m_activationInProgress = false;
            }
            if (releaseCandidate)
                Attempt(() => owner.ReleaseSnapshot(candidate), failures);
            previous = null;
            candidate = null!;
            ThrowCleanupFailures(failures);
        }

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void Rollback()
        {
            owner.EnsureRetirementSafe();
            bool ownerWasDisposed;
            lock (owner.m_sync)
            {
                if (m_finished)
                    return;
                ownerWasDisposed = owner.m_disposed;
                if (m_activated && !ownerWasDisposed)
                {
                    owner.m_current = previous;
                    owner.m_typeCacheVersion = previousVersion;
                }
                else if (ownerWasDisposed)
                {
                    owner.m_current = null;
                    owner.m_typeCacheVersion = -1;
                }
                owner.m_activationInProgress = false;
                m_finished = true;
            }

            List<Exception> failures = [];
            if (m_activationStarted)
                Attempt(() => owner.RollbackActivation(previous, candidate), failures);
            Attempt(() => owner.ReleaseSnapshot(candidate), failures);
            if (ownerWasDisposed && previous is not null)
                Attempt(() => owner.ReleaseSnapshot(previous), failures);
            previous = null;
            candidate = null!;
            ThrowCleanupFailures(failures);
        }

        private static void Attempt(Action action, List<Exception> failures)
        {
            try { action(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
            {
                if (failures.Count > 0)
                    throw new AggregateException("Registry retirement remains pending after earlier cleanup failures.", [.. failures, pendingRetirement]);
                throw;
            }
            catch (Exception exception) { failures.Add(exception); }
        }

        private static void ThrowCleanupFailures(List<Exception> failures)
        {
            if (failures.Count > 0)
                throw new AggregateException("Registry retirement or rollback failed after every resource was attempted.", failures);
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Registry transaction is already finished.");
        }
    }
}
