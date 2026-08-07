using System;
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
    /// Registers an identity object.
    /// </summary>
    /// <param name="obj">Identity object to register.</param>
    /// <param name="persistentId">Optional explicit persistent id override.</param>
    /// <returns><see langword="true"/> when newly registered.</returns>
    public static bool Register(IIdentityObject obj, Guid? persistentId = null)
        => s_registry.Register(obj, persistentId);

    /// <summary>
    /// Unregisters an identity object.
    /// </summary>
    /// <param name="obj">Identity object to unregister.</param>
    /// <returns><see langword="true"/> when unregistered.</returns>
    public static bool Unregister(IIdentityObject obj)
        => s_registry.Unregister(obj);

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
