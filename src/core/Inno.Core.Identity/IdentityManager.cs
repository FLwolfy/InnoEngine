using System;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Identity;

/// <summary>
/// Global runtime identity dispatcher for all identity-enabled objects.
/// </summary>
public static class IdentityManager
{
    private static readonly Lock C_LOCK = new();
    private static volatile IdentityRegistry s_registry = new();

    /// <summary>
    /// Returns whether the runtime registry has been initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Occurs after an identity object has been removed from the runtime registry.
    /// </summary>
    /// <remarks>
    /// The registry change is permanent before handlers run. All handlers are invoked and
    /// handler failures are reported together as an <see cref="AggregateException"/>.
    /// </remarks>
    public static event Action<IIdentityObject>? ObjectUnregistered;

    /// <summary>
    /// Initializes (or resets) the identity registry used for runtime identity dispatch.
    /// </summary>
    public static void Initialize()
    {
        lock (C_LOCK)
        {
            s_registry = new IdentityRegistry();
            isInitialized = true;
        }
    }

    /// <summary>
    /// Releases the current runtime registry and marks identity services as uninitialized.
    /// </summary>
    public static void Shutdown()
    {
        lock (C_LOCK)
        {
            s_registry = new IdentityRegistry();
            isInitialized = false;
        }
    }

    /// <summary>
    /// Registers an identity object.
    /// </summary>
    /// <param name="obj">Identity object to register.</param>
    /// <param name="persistentId">Optional explicit persistent id override.</param>
    /// <returns><see langword="true"/> when newly registered.</returns>
    public static bool Register(IIdentityObject obj, Guid? persistentId = null)
        => s_registry.Register(obj, persistentId);

    /// <summary>
    /// Assigns a persistent identity to an object without registering a runtime identity.
    /// </summary>
    /// <param name="obj">Detached identity object to initialize.</param>
    /// <param name="persistentId">Non-empty persistent identity to assign.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="obj"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="persistentId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the object is currently registered.</exception>
    public static void InitializePersistentIdentity(IIdentityObject obj, Guid persistentId)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (persistentId == Guid.Empty)
            throw new ArgumentException("A detached persistent identity cannot be empty.", nameof(persistentId));
        if (obj.GetIdentity().runtimeId is not null)
            throw new InvalidOperationException("A registered identity object cannot be reinitialized.");
        obj.SetIdentity(new Identity(persistentId));
    }

    /// <summary>
    /// Unregisters an identity object.
    /// </summary>
    /// <param name="obj">Identity object to unregister.</param>
    /// <returns><see langword="true"/> when unregistered.</returns>
    public static bool Unregister(IIdentityObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!s_registry.Unregister(obj))
            return false;

        Action<IIdentityObject>? handlers = ObjectUnregistered;
        if (handlers is null)
            return true;

        List<Exception>? failures = null;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<IIdentityObject>)handler)(obj);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more identity unregistration handlers failed.", failures);
        return true;
    }

    /// <summary>
    /// Resolves an identity object by runtime id.
    /// </summary>
    public static TIdentity? Get<TIdentity>(int runtimeId)
        where TIdentity : class, IIdentityObject
        => s_registry.Get<TIdentity>(runtimeId);

    /// <summary>
    /// Resolves an identity object by persistent id.
    /// </summary>
    public static TIdentity? Get<TIdentity>(Guid persistentId)
        where TIdentity : class, IIdentityObject
        => s_registry.Get<TIdentity>(persistentId);

    internal static bool TryGetRuntimeId(in Identity identity, out int runtimeId)
        => s_registry.TryGetRuntimeId(identity, out runtimeId);
}
