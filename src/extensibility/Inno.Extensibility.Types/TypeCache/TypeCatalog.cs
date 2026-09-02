using System;
using System.Collections.Generic;
using System.Threading;

using Inno.Extensibility.Modules;

namespace Inno.Extensibility.Types;

/// <summary>
/// Owns the active type catalog and provides all type discovery and identity queries.
/// </summary>
public sealed class TypeCatalog : IDisposable
{
    private readonly object m_sync = new();
    private readonly ModuleHost m_modules;
    private readonly TypeRegistryCoordinator m_registries = new();
    private readonly TypeCacheCatalogParticipant m_participant;
    private readonly IDisposable m_participantRegistration;

    private TypeCacheSnapshot m_current = TypeCacheSnapshot.empty;
    private long m_nextVersion;
    private bool m_disposed;

    /// <summary>
    /// Creates a type catalog derived transactionally from one module host.
    /// </summary>
    /// <param name="modules">
    /// The module host that owns the assembly generations visible to this catalog.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="modules"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="modules"/> has already been disposed.
    /// </exception>
    public TypeCatalog(ModuleHost modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (!modules.isInitialized)
            throw new InvalidOperationException("The module host must be active before creating a type catalog.");
        m_modules = modules;
        m_participant = new TypeCacheCatalogParticipant(this);
        m_participantRegistration = modules.RegisterCatalogParticipant(m_participant);
    }

    /// <summary>
    /// Gets whether the type catalog is registered with an initialized <see cref="ModuleHost"/>.
    /// </summary>
    public bool isInitialized => !m_disposed && m_modules.isInitialized;

    /// <summary>
    /// Gets the current immutable type snapshot after applying pending host assembly changes.
    /// </summary>
    public TypeCacheSnapshot current
    {
        get
        {
            EnsureInitialized();
            m_modules.Refresh();
            lock (m_sync)
                return m_current;
        }
    }

    /// <summary>
    /// Rebuilds the assembly catalog, type snapshot, and every registered type registry.
    /// </summary>
    public void Rebuild()
    {
        EnsureInitialized();
        m_modules.Rebuild();
    }

    /// <summary>
    /// Unregisters type discovery and releases all active type-registry snapshots.
    /// </summary>
    public void Dispose()
    {
        TypeRegistryRefreshSet registries;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_disposed = true;
            registries = m_registries.Prepare(TypeCacheSnapshot.empty);
            m_current = TypeCacheSnapshot.empty;
            m_nextVersion = 0;
        }

        registries.Activate();
        registries.Complete();
        m_participantRegistration.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets all non-abstract discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The required base type.
    /// </typeparam>
    /// <returns>
    /// The matching concrete types in stable catalog order.
    /// </returns>
    public IReadOnlyList<TypeRef> GetSubTypesOf<T>() => current.GetSubTypesOf<T>();

    /// <summary>
    /// Gets all non-abstract discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">
    /// The required interface.
    /// </typeparam>
    /// <returns>
    /// The matching concrete types in stable catalog order.
    /// </returns>
    public IReadOnlyList<TypeRef> GetTypesImplementing<TInterface>()
        => current.GetTypesImplementing<TInterface>();

    /// <summary>
    /// Gets all non-abstract discovered types marked with <typeparamref name="TAttribute"/>.
    /// </summary>
    /// <typeparam name="TAttribute">
    /// The required attribute type.
    /// </typeparam>
    /// <returns>
    /// The matching concrete types in stable catalog order.
    /// </returns>
    public IReadOnlyList<TypeRef> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        => current.GetTypesWithAttribute<TAttribute>();

    /// <summary>
    /// Gets the reference for an active CLR type.
    /// </summary>
    /// <returns>
    /// Its logical and generation-local identity.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="type"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the type does not belong to the active catalog.
    /// </exception>
    /// <param name="type">
    /// The type consumed by get type ref; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public TypeRef GetTypeRef(Type type) => current.GetTypeRef(type);

    /// <summary>
    /// Tries to get the reference for an active CLR type.
    /// </summary>
    /// <param name="type">
    /// The active CLR type.
    /// </param>
    /// <param name="typeRef">
    /// Receives its logical and generation-local identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type belongs to the active catalog.
    /// </returns>
    public bool TryGetTypeRef(Type type, out TypeRef typeRef)
        => current.TryGetTypeRef(type, out typeRef);

    /// <summary>
    /// Attempts to resolve a logical type reference against the active immutable generation.
    /// </summary>
    /// <param name="typeRef">
    /// The stable type reference to resolve.
    /// </param>
    /// <param name="type">
    /// Receives the active CLR type when resolution succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the active generation contains the logical type.
    /// </returns>
    public bool TryResolve(TypeRef typeRef, out Type? type)
    {
        if (!isInitialized)
        {
            type = null;
            return false;
        }
        return current.TryResolve(typeRef, out type);
    }

    /// <summary>
    /// Resolves a logical type reference against the active immutable generation.
    /// </summary>
    /// <param name="typeRef">
    /// The stable type reference to resolve.
    /// </param>
    /// <returns>
    /// The CLR type owned by the active generation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the reference is empty or unavailable in the active generation.
    /// </exception>
    public Type Resolve(TypeRef typeRef)
    {
        if (typeRef.stableId != Guid.Empty && TryResolve(typeRef, out Type? type))
            return type!;
        throw new InvalidOperationException(
            typeRef.stableId == Guid.Empty
                ? "An empty type reference cannot be resolved."
                : $"Type reference '{typeRef.stableId:D}' is not available in the active type cache.");
    }

    private void EnsureInitialized()
    {
        if (!isInitialized)
            throw new InvalidOperationException("TypeCatalog is not initialized.");
    }

    internal TypeRegistryRegistration Register(ITypeRegistry registry)
    {
        EnsureInitialized();
        return m_registries.Register(registry);
    }

    private sealed class TypeCacheCatalogParticipant(TypeCatalog owner) : IAssemblyCatalogParticipant
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        /// <param name="catalog">
        /// The candidate asset catalog prepared for activation.
        /// </param>
        /// <returns>
        /// The validated iassembly catalog transaction that represents the completed operation.
        /// </returns>
        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog)
        {
            TypeCacheSnapshot previous;
            lock (owner.m_sync)
                previous = owner.m_current;
            TypeCacheSnapshot candidate = TypeCacheSnapshot.Build(
                catalog.assemblies,
                previous,
                Interlocked.Increment(ref owner.m_nextVersion));
            TypeRegistryRefreshSet registries = owner.m_registries.Prepare(candidate);
            return new TypeCacheCatalogTransaction(owner, previous, candidate, registries);
        }
    }

    private sealed class TypeCacheCatalogTransaction(
        TypeCatalog owner,
        TypeCacheSnapshot previous,
        TypeCacheSnapshot candidate,
        TypeRegistryRefreshSet registries) : IAssemblyCatalogTransaction
    {
        private readonly TypeCacheReloadContext m_context = new(previous, candidate);
        private bool m_activated;
        private bool m_finished;

        /// <summary>
        /// Gets the candidate activation context shared with participating registries.
        /// </summary>
        public object context => m_context;

        /// <summary>
        /// Makes the prepared value active at the owning subsystem's safety point.
        /// </summary>
        public void Activate()
        {
            EnsureNotFinished();
            lock (owner.m_sync)
                owner.m_current = candidate;
            try
            {
                registries.Activate();
                m_activated = true;
            }
            catch
            {
                lock (owner.m_sync)
                    owner.m_current = previous;
                registries.Rollback();
                m_finished = true;
                m_context.Release();
                throw;
            }
        }

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete()
        {
            EnsureNotFinished();
            if (!m_activated)
                throw new InvalidOperationException("Type cache transaction has not been activated.");
            registries.Complete();
            m_finished = true;
            m_context.Release();
        }

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void Rollback()
        {
            if (m_finished)
                return;
            if (m_activated)
            {
                lock (owner.m_sync)
                    owner.m_current = previous;
            }
            registries.Rollback();
            m_finished = true;
            m_context.Release();
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Type cache transaction is already finished.");
        }
    }
}
