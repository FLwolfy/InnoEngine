using System;
using System.Diagnostics;

using Inno.Core.Reflection.Internal;

namespace Inno.Core.Reflection;

/// <summary>
/// Builds immutable extension registries from versioned type-cache snapshots.
/// </summary>
/// <typeparam name="TSnapshot">The immutable registry snapshot type.</typeparam>
public abstract class TypeRegistry<TSnapshot> : IDisposable
    where TSnapshot : class
{
    private readonly object m_sync = new();
    private readonly RegistryAdapter m_registryAdapter;
    private readonly TypeRegistryRegistration m_registration;

    private TSnapshot? m_current;
    private long m_typeCacheVersion = -1;
    private bool m_activationInProgress;
    private bool m_refreshPending;
    private bool m_disposed;

    /// <summary>
    /// Creates and registers a refreshable type registry.
    /// </summary>
    protected TypeRegistry()
    {
        m_registryAdapter = new RegistryAdapter(this);
        m_registration = TypeRegistryCoordinator.Register(m_registryAdapter);
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
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ITypeRegistryTransaction transaction = Prepare(TypeCacheManager.current, allowDisposed: false);
        transaction.Activate();
        transaction.Complete();
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
            snapshot = m_current;
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
            TypeCacheSnapshot types = TypeCacheManager.current;
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
    /// <param name="types">The candidate type-cache snapshot.</param>
    /// <returns>The complete immutable registry snapshot.</returns>
    protected abstract TSnapshot Build(TypeCacheSnapshot types);

    /// <summary>
    /// Reversibly activates a complete candidate while the previous snapshot remains available.
    /// </summary>
    /// <param name="previous">The previous active snapshot, or <see langword="null"/> during initial activation.</param>
    /// <param name="candidate">The candidate exposed through <see cref="current"/> during this callback.</param>
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
    /// <param name="previous">The restored active snapshot, or <see langword="null"/> when none existed.</param>
    /// <param name="candidate">The rejected candidate snapshot.</param>
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
    /// <param name="previous">The previous snapshot that is about to be released, or <see langword="null"/>.</param>
    /// <param name="currentSnapshot">The committed active snapshot.</param>
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
    /// <param name="snapshot">The snapshot that is no longer active.</param>
    protected virtual void DisposeSnapshot(TSnapshot snapshot)
    {
        if (snapshot is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Reports an exception raised while rolling back activation or releasing a snapshot.
    /// </summary>
    /// <param name="phase">The cleanup phase that raised the exception.</param>
    /// <param name="exception">The cleanup exception.</param>
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
    /// <typeparam name="TExtension">The required extension contract.</typeparam>
    /// <param name="type">The concrete implementation type.</param>
    /// <returns>A newly created extension instance.</returns>
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
            {
                if (m_typeCacheVersion != types.version)
                    m_refreshPending = true;
                return TypeRegistryNoopTransaction.instance;
            }
            if (m_current is not null && m_typeCacheVersion == types.version)
                return TypeRegistryNoopTransaction.instance;

            TSnapshot candidate = Build(types);
            return new RegistryTransaction(this, candidate, types.version, m_current, m_typeCacheVersion);
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

        public void Activate()
        {
            lock (owner.m_sync)
            {
                EnsureNotFinished();
                ObjectDisposedException.ThrowIf(owner.m_disposed, owner);
                owner.m_current = candidate;
                owner.m_typeCacheVersion = candidateVersion;
                owner.m_activationInProgress = true;
                m_activated = true;
                m_activationStarted = true;
            }

            try
            {
                owner.OnActivating(previous, candidate);
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        public void Complete()
        {
            TSnapshot? snapshotToRelease;
            bool refreshPending;
            lock (owner.m_sync)
            {
                EnsureNotFinished();
                if (!m_activated)
                    throw new InvalidOperationException("Registry transaction has not been activated.");
                m_finished = true;
                snapshotToRelease = previous;
            }

            owner.CompleteActivation(previous, candidate);
            if (snapshotToRelease is not null)
                owner.ReleaseSnapshot(snapshotToRelease);
            lock (owner.m_sync)
            {
                owner.m_activationInProgress = false;
                refreshPending = owner.m_refreshPending;
                owner.m_refreshPending = false;
            }
            if (refreshPending && !owner.m_disposed && TypeCacheManager.isInitialized)
                owner.Refresh();
        }

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
                owner.m_activationInProgress = false;
                m_finished = true;
            }

            if (ownerWasDisposed && m_activated)
            {
                if (previous is not null)
                    owner.ReleaseSnapshot(previous);
            }
            else
            {
                if (m_activationStarted)
                    owner.RollbackActivation(previous, candidate);
                owner.ReleaseSnapshot(candidate);
            }
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Registry transaction is already finished.");
        }
    }
}
