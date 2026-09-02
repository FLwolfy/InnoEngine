using System;
using System.Diagnostics;

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

    /// <summary>
    /// Creates and registers a refreshable type registry.
    /// </summary>
    /// <param name="types">
    /// The type catalog that coordinates this registry's candidate generations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="types"/> is null.
    /// </exception>
    protected TypeRegistry(TypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(types);
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
            catch
            {
                transaction.Rollback();
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
    public void Clear()
    {
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
            m_current = null;
            m_typeCacheVersion = -1;
        }

        if (snapshot is not null)
            ReleaseSnapshot(snapshot);
    }

    /// <summary>
    /// Releases the current registry snapshot.
    /// </summary>
    public void Dispose()
    {
        TSnapshot? snapshot;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            snapshot = m_activationInProgress ? null : m_current;
            m_current = null;
            m_typeCacheVersion = -1;
        }

        m_registration.Dispose();
        if (snapshot is not null)
            ReleaseSnapshot(snapshot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the current snapshot, refreshing it when the type catalog changed.
    /// </summary>
    protected TSnapshot current
    {
        get
        {
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
    /// Exceptions are reported through <see cref="OnCleanupFailed"/> and do not prevent other registries
    /// from rolling back.
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
    /// Creates a validated extension instance using a parameterless constructor.
    /// </summary>
    /// <typeparam name="TExtension">
    /// The required extension contract.
    /// </typeparam>
    /// <param name="type">
    /// The concrete implementation type.
    /// </param>
    /// <returns>
    /// A newly created extension instance.
    /// </returns>
    protected static TExtension CreateExtension<TExtension>(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsAbstract || !typeof(TExtension).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Extension '{type.FullName}' must be a non-abstract {typeof(TExtension).FullName}.");
        }

        try
        {
            return (TExtension)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException("Activator returned null."));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Extension '{type.FullName}' requires a parameterless constructor.",
                exception);
        }
    }

    private ITypeRegistryTransaction Prepare(TypeCacheSnapshot types, bool allowDisposed)
    {
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
            catch
            {
                m_activationInProgress = false;
                throw;
            }
        }
    }

    private void RollbackActivation(TSnapshot? previous, TSnapshot candidate)
    {
        try
        {
            OnActivationRolledBack(previous, candidate);
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("activation rollback", exception);
        }
    }

    private void ReleaseSnapshot(TSnapshot snapshot)
    {
        try
        {
            DisposeSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("snapshot release", exception);
        }
    }

    private void CompleteActivation(TSnapshot? previous, TSnapshot candidate)
    {
        try
        {
            OnActivationCompleted(previous, candidate);
        }
        catch (Exception exception)
        {
            ReportCleanupFailure("activation completion", exception);
        }
    }

    private void ReportCleanupFailure(string phase, Exception exception)
    {
        try
        {
            OnCleanupFailed(phase, exception);
        }
        catch
        {
        }
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
            catch
            {
                Rollback();
                throw;
            }
        }

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete()
        {
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

            owner.CompleteActivation(previous, candidate);
            if (snapshotToRelease is not null)
                owner.ReleaseSnapshot(snapshotToRelease);
            bool releaseCandidate;
            lock (owner.m_sync)
            {
                releaseCandidate = owner.m_disposed;
                owner.m_activationInProgress = false;
            }
            if (releaseCandidate)
                owner.ReleaseSnapshot(candidate);
            previous = null;
            candidate = null!;
        }

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void Rollback()
        {
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

            if (m_activationStarted)
                owner.RollbackActivation(previous, candidate);
            owner.ReleaseSnapshot(candidate);
            if (ownerWasDisposed && previous is not null)
                owner.ReleaseSnapshot(previous);
            previous = null;
            candidate = null!;
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Registry transaction is already finished.");
        }
    }
}
