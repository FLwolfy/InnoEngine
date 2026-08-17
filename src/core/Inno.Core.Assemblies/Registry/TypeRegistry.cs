using System;

using Inno.Core.Assemblies.Internal;
using Inno.Core.Reflection;

namespace Inno.Core.Assemblies;

/// <summary>
/// Builds immutable extension registries from versioned type-cache snapshots.
/// </summary>
/// <typeparam name="TSnapshot">The immutable registry snapshot type.</typeparam>
public abstract class TypeRegistry<TSnapshot> : IDisposable
    where TSnapshot : class
{
    private readonly object m_sync = new();
    private readonly RegistryAdapter m_registryAdapter;
    private readonly RegistryRegistration m_registration;

    private TSnapshot? m_current;
    private long m_typeCacheVersion = -1;
    private bool m_disposed;

    /// <summary>
    /// Creates and registers a refreshable type registry.
    /// </summary>
    protected TypeRegistry()
    {
        m_registryAdapter = new RegistryAdapter(this);
        m_registration = RegistryCoordinator.Register(m_registryAdapter);
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
        IRegistryRefreshTransaction transaction = Prepare(TypeCache.current, allowDisposed: false);
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
            DisposeSnapshot(snapshot);
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
            DisposeSnapshot(snapshot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the current snapshot, refreshing it when the type catalog changed.
    /// </summary>
    protected TSnapshot current
    {
        get
        {
            TypeCacheSnapshot types = TypeCache.current;
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
    protected abstract TSnapshot Build(TypeCacheSnapshot types);

    /// <summary>
    /// Runs after a new snapshot is committed and before the previous snapshot is released.
    /// </summary>
    protected virtual void OnCommitted(TSnapshot previous, TSnapshot current)
    {
    }

    /// <summary>
    /// Releases resources owned by a registry snapshot.
    /// </summary>
    protected virtual void DisposeSnapshot(TSnapshot snapshot)
    {
        if (snapshot is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Creates a validated extension instance using a parameterless constructor.
    /// </summary>
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

    private IRegistryRefreshTransaction Prepare(TypeCacheSnapshot types, bool allowDisposed)
    {
        lock (m_sync)
        {
            if (m_disposed)
            {
                if (allowDisposed)
                    return RegistryRefreshTransaction.noop;
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (m_current is not null && m_typeCacheVersion == types.version)
                return RegistryRefreshTransaction.noop;

            TSnapshot candidate = Build(types);
            return new RegistryTransaction(this, candidate, types.version, m_current, m_typeCacheVersion);
        }
    }

    private sealed class RegistryAdapter(TypeRegistry<TSnapshot> owner) : ITypeRegistry
    {
        public IRegistryRefreshTransaction Prepare(TypeCacheSnapshot types)
            => owner.Prepare(types, allowDisposed: true);
    }

    private sealed class RegistryTransaction(
        TypeRegistry<TSnapshot> owner,
        TSnapshot candidate,
        long candidateVersion,
        TSnapshot? previous,
        long previousVersion) : IRegistryRefreshTransaction
    {
        private bool m_activated;
        private bool m_finished;

        public void Activate()
        {
            lock (owner.m_sync)
            {
                EnsureNotFinished();
                ObjectDisposedException.ThrowIf(owner.m_disposed, owner);
                owner.m_current = candidate;
                owner.m_typeCacheVersion = candidateVersion;
                m_activated = true;
            }
        }

        public void Complete()
        {
            bool ownerWasDisposed;
            lock (owner.m_sync)
            {
                EnsureNotFinished();
                if (!m_activated)
                    throw new InvalidOperationException("Registry transaction has not been activated.");
                ownerWasDisposed = owner.m_disposed;
                m_finished = true;
            }

            if (previous is not null)
            {
                if (!ownerWasDisposed)
                    owner.OnCommitted(previous, candidate);
                owner.DisposeSnapshot(previous);
            }
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
                m_finished = true;
            }

            if (ownerWasDisposed && m_activated)
            {
                if (previous is not null)
                    owner.DisposeSnapshot(previous);
            }
            else
            {
                owner.DisposeSnapshot(candidate);
            }
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Registry transaction is already finished.");
        }
    }
}
