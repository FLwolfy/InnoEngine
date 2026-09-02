using System;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Identity;

/// <summary>
/// Owns the persistent-to-runtime identity map for one isolated runtime session.
/// </summary>
public sealed class IdentityAllocator
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    private readonly IdentityRegistry m_registry = new();

    /// <summary>
    /// Gets whether the current asynchronous execution context is bound to an allocator.
    /// </summary>
    public static bool hasCurrent => S_CURRENT_SCOPE.Value is not null;

    /// <summary>
    /// Gets the allocator bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the caller is outside a runtime session execution scope.
    /// </exception>
    public static IdentityAllocator current
        => S_CURRENT_SCOPE.Value?.allocator
            ?? throw new InvalidOperationException(
                "No identity allocator is bound to the current runtime execution context.");

    /// <summary>
    /// Gets the number of currently registered live identity objects.
    /// </summary>
    public int count => m_registry.count;

    /// <summary>
    /// Occurs after an object has been permanently removed from this allocator.
    /// </summary>
    /// <remarks>
    /// Every observer is invoked. Multiple observer failures are reported as one
    /// <see cref="AggregateException"/> after notification completes.
    /// </remarks>
    public event Action<IdentityObject>? ObjectUnregistered;

    /// <summary>
    /// Binds this allocator to the current asynchronous execution context until the returned scope is disposed.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out scope owned by the caller.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when scopes are disposed out of order.
    /// </exception>
    public IDisposable EnterScope()
    {
        var scope = new Scope(this, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    /// <summary>
    /// Registers an object and assigns its session-local runtime identity.
    /// </summary>
    /// <param name="obj">
    /// The identity object owned by this runtime session.
    /// </param>
    /// <param name="persistentId">
    /// An optional explicit persistent identity; an identity is generated when omitted or empty.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object was newly registered; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Register(IdentityObject obj, Guid? persistentId = null)
        => m_registry.Register(obj, persistentId);

    /// <summary>
    /// Assigns a persistent identity to a detached object without allocating a runtime identity.
    /// </summary>
    /// <param name="obj">
    /// The detached identity object to initialize.
    /// </param>
    /// <param name="persistentId">
    /// The non-empty persistent identity to assign.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="obj"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="persistentId"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the object already has a live runtime identity.
    /// </exception>
    public void InitializePersistentIdentity(IdentityObject obj, Guid persistentId)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (persistentId == Guid.Empty)
            throw new ArgumentException("A detached persistent identity cannot be empty.", nameof(persistentId));
        if (obj.identity.runtimeId is not null)
            throw new InvalidOperationException("A registered identity object cannot be reinitialized.");
        obj.SetIdentity(new Identity(persistentId));
    }

    /// <summary>
    /// Removes an object from this session and notifies every unregistration observer.
    /// </summary>
    /// <param name="obj">
    /// The identity object to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object was registered; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="AggregateException">
    /// Thrown after removal when one or more observers fail.
    /// </exception>
    public bool Unregister(IdentityObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!m_registry.Unregister(obj))
            return false;
        Action<IdentityObject>? handlers = ObjectUnregistered;
        if (handlers is null)
            return true;
        List<Exception>? failures = null;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<IdentityObject>)handler)(obj);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more identity unregistration observers failed.", failures);
        return true;
    }

    /// <summary>
    /// Resolves a registered object by its session-local runtime identity.
    /// </summary>
    /// <typeparam name="TIdentity">
    /// The required identity object contract.
    /// </typeparam>
    /// <param name="runtimeId">
    /// The packed runtime identity allocated by this instance.
    /// </param>
    /// <returns>
    /// The live compatible object, or <see langword="null"/> when no matching object exists.
    /// </returns>
    public TIdentity? Get<TIdentity>(int runtimeId)
        where TIdentity : IdentityObject
        => m_registry.Get<TIdentity>(runtimeId);

    /// <summary>
    /// Resolves a registered object by its persistent identity.
    /// </summary>
    /// <typeparam name="TIdentity">
    /// The required identity object contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent identity to resolve.
    /// </param>
    /// <returns>
    /// The live compatible object, or <see langword="null"/> when no matching object exists.
    /// </returns>
    public TIdentity? Get<TIdentity>(Guid persistentId)
        where TIdentity : IdentityObject
        => m_registry.Get<TIdentity>(persistentId);

    private sealed class Scope(IdentityAllocator allocator, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal IdentityAllocator allocator { get; } = allocator;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
                throw new InvalidOperationException("Identity execution scopes must be disposed in reverse order.");
            m_disposed = true;
            S_CURRENT_SCOPE.Value = parent;
        }
    }
}
