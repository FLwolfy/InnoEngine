using System;
using System.Collections.Generic;
using System.Threading;

using Inno.Core.Assemblies;

namespace Inno.Core.Reflection;

/// <summary>
/// Owns the active type catalog and provides all type discovery and identity queries.
/// </summary>
public static class TypeCacheManager
{
    private static readonly object S_SYNC = new();
    private static readonly TypeCacheCatalogParticipant S_PARTICIPANT = new();

    private static TypeCacheSnapshot s_current = TypeCacheSnapshot.empty;
    private static IDisposable? s_participantRegistration;
    private static long s_nextVersion;
    private static bool s_isInitialized;

    /// <summary>
    /// Gets whether the type catalog is registered with an initialized <see cref="AssemblyManager"/>.
    /// </summary>
    public static bool isInitialized => s_isInitialized && AssemblyManager.isInitialized;

    /// <summary>
    /// Gets the current immutable type snapshot after applying pending host assembly changes.
    /// </summary>
    public static TypeCacheSnapshot current
    {
        get
        {
            EnsureInitialized();
            AssemblyManager.Refresh();
            lock (S_SYNC)
                return s_current;
        }
    }

    /// <summary>
    /// Registers type discovery and all <see cref="TypeRegistry{TSnapshot}"/> instances as one
    /// transactional participant of the assembly catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="AssemblyManager"/> has not been initialized.
    /// </exception>
    public static void Initialize()
    {
        lock (S_SYNC)
        {
            if (s_isInitialized)
                return;
            if (!AssemblyManager.isInitialized)
            {
                throw new InvalidOperationException(
                    "AssemblyManager must be initialized before TypeCacheManager.");
            }

            s_participantRegistration = AssemblyManager.RegisterCatalogParticipant(S_PARTICIPANT);
            s_isInitialized = true;
        }
    }

    /// <summary>
    /// Rebuilds the assembly catalog, type snapshot, and every registered type registry.
    /// </summary>
    public static void Rebuild()
    {
        EnsureInitialized();
        AssemblyManager.Rebuild();
    }

    /// <summary>
    /// Unregisters type discovery and releases all active type-registry snapshots.
    /// </summary>
    public static void Shutdown()
    {
        IDisposable? registration;
        TypeRegistryRefreshSet registries;
        lock (S_SYNC)
        {
            if (!s_isInitialized)
                return;
            registration = s_participantRegistration;
            s_participantRegistration = null;
            registries = TypeRegistryCoordinator.Prepare(TypeCacheSnapshot.empty);
            s_current = TypeCacheSnapshot.empty;
            s_isInitialized = false;
            s_nextVersion = 0;
        }

        registries.Activate();
        registries.Complete();
        registration?.Dispose();
    }

    /// <summary>
    /// Gets all non-abstract discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The required base type.</typeparam>
    /// <returns>The matching concrete types in stable catalog order.</returns>
    public static IReadOnlyList<TypeRef> GetSubTypesOf<T>() => current.GetSubTypesOf<T>();

    /// <summary>
    /// Gets all non-abstract discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The required interface.</typeparam>
    /// <returns>The matching concrete types in stable catalog order.</returns>
    public static IReadOnlyList<TypeRef> GetTypesImplementing<TInterface>()
        => current.GetTypesImplementing<TInterface>();

    /// <summary>
    /// Gets all non-abstract discovered types marked with <typeparamref name="TAttribute"/>.
    /// </summary>
    /// <typeparam name="TAttribute">The required attribute type.</typeparam>
    /// <returns>The matching concrete types in stable catalog order.</returns>
    public static IReadOnlyList<TypeRef> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        => current.GetTypesWithAttribute<TAttribute>();

    /// <summary>
    /// Gets the reference for an active CLR type.
    /// </summary>
    /// <param name="type">The active CLR type.</param>
    /// <returns>Its logical and generation-local identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the type does not belong to the active catalog.</exception>
    public static TypeRef GetTypeRef(Type type) => current.GetTypeRef(type);

    /// <summary>
    /// Tries to get the reference for an active CLR type.
    /// </summary>
    /// <param name="type">The active CLR type.</param>
    /// <param name="typeRef">Receives its logical and generation-local identity.</param>
    /// <returns><see langword="true"/> when the type belongs to the active catalog.</returns>
    public static bool TryGetTypeRef(Type type, out TypeRef typeRef)
        => current.TryGetTypeRef(type, out typeRef);

    internal static bool TryResolveCurrent(TypeRef typeRef, out Type? type)
    {
        if (!isInitialized)
        {
            type = null;
            return false;
        }
        return current.TryResolve(typeRef, out type);
    }

    internal static Type ResolveCurrent(TypeRef typeRef)
    {
        if (typeRef.stableId != Guid.Empty && TryResolveCurrent(typeRef, out Type? type))
            return type!;
        throw new InvalidOperationException(
            typeRef.stableId == Guid.Empty
                ? "An empty type reference cannot be resolved."
                : $"Type reference '{typeRef.stableId:D}' is not available in the active type cache.");
    }

    private static void EnsureInitialized()
    {
        if (!isInitialized)
            throw new InvalidOperationException("TypeCacheManager is not initialized.");
    }

    private sealed class TypeCacheCatalogParticipant : IAssemblyCatalogParticipant
    {
        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog)
        {
            TypeCacheSnapshot previous;
            lock (S_SYNC)
                previous = s_current;
            TypeCacheSnapshot candidate = TypeCacheSnapshot.Build(
                catalog.assemblies,
                previous,
                Interlocked.Increment(ref s_nextVersion));
            TypeRegistryRefreshSet registries = TypeRegistryCoordinator.Prepare(candidate);
            return new TypeCacheCatalogTransaction(previous, candidate, registries);
        }
    }

    private sealed class TypeCacheCatalogTransaction(
        TypeCacheSnapshot previous,
        TypeCacheSnapshot candidate,
        TypeRegistryRefreshSet registries) : IAssemblyCatalogTransaction
    {
        private readonly TypeCacheReloadContext m_context = new(previous, candidate);
        private bool m_activated;
        private bool m_finished;

        public object context => m_context;

        public void Activate()
        {
            EnsureNotFinished();
            lock (S_SYNC)
                s_current = candidate;
            try
            {
                registries.Activate();
                m_activated = true;
            }
            catch
            {
                lock (S_SYNC)
                    s_current = previous;
                registries.Rollback();
                m_finished = true;
                m_context.Release();
                throw;
            }
        }

        public void Complete()
        {
            EnsureNotFinished();
            if (!m_activated)
                throw new InvalidOperationException("Type cache transaction has not been activated.");
            registries.Complete();
            m_finished = true;
            m_context.Release();
        }

        public void Rollback()
        {
            if (m_finished)
                return;
            if (m_activated)
            {
                lock (S_SYNC)
                    s_current = previous;
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
