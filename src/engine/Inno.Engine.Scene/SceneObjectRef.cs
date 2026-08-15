using System;

using Inno.Core.Identity;

namespace Inno.Engine.Scene;

/// <summary>
/// Stores a persistent reference to an identity-enabled scene object.
/// </summary>
/// <typeparam name="TObject">Referenced scene object type.</typeparam>
public readonly struct SceneObjectRef<TObject>
    where TObject : class, IIdentityObject
{
    /// <summary>
    /// Gets the referenced persistent identifier.
    /// </summary>
    public Guid persistentId { get; }

    /// <summary>
    /// Gets whether this handle contains a persistent identifier.
    /// </summary>
    public bool isValid => persistentId != Guid.Empty;

    /// <summary>
    /// Gets whether this handle has an identifier that cannot currently be resolved.
    /// </summary>
    public bool isMissing => isValid && !TryResolve(out _);

    /// <summary>
    /// Creates a reference from a persistent identifier.
    /// </summary>
    /// <param name="persistentId">Referenced persistent identifier.</param>
    public SceneObjectRef(Guid persistentId)
    {
        this.persistentId = persistentId;
    }

    /// <summary>
    /// Creates a reference from a live object.
    /// </summary>
    /// <param name="target">Referenced object, or <see langword="null"/> for an empty handle.</param>
    public SceneObjectRef(TObject? target)
    {
        persistentId = target?.GetIdentity().persistentId ?? Guid.Empty;
    }

    /// <summary>
    /// Tries to resolve the current live object.
    /// </summary>
    /// <param name="target">Resolved object when available.</param>
    /// <returns><see langword="true"/> when a compatible live object was found.</returns>
    public bool TryResolve(out TObject? target)
    {
        target = isValid ? IdentityManager.Get<TObject>(persistentId) : null;
        return target is not null;
    }

    /// <summary>
    /// Returns a readable reference state.
    /// </summary>
    public override string ToString()
    {
        if (!isValid)
        {
            return $"{typeof(TObject).Name} (None)";
        }

        return TryResolve(out TObject? target)
            ? target!.ToString() ?? typeof(TObject).Name
            : $"{typeof(TObject).Name} (Missing: {persistentId})";
    }
}
