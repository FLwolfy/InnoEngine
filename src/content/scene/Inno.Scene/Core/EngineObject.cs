using System;
using System.Runtime.CompilerServices;

using Inno.Core.Identity;

namespace Inno.Scene;

/// <summary>
/// Provides identity and destruction state shared by all managed scene objects.
/// </summary>
public abstract class EngineObject : IdentityObject
{
    private bool m_isDestroyed;

    /// <summary>
    /// Gets whether the engine has destroyed this object.
    /// </summary>
    public bool isDestroyed => m_isDestroyed;

    /// <summary>
    /// Compares engine objects using reference identity and destroyed-object null semantics.
    /// </summary>
    /// <param name="left">
    /// Left operand.
    /// </param>
    /// <param name="right">
    /// Right operand.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both operands refer to the same live object or both behave as null.
    /// </returns>
    public static bool operator ==(EngineObject? left, EngineObject? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (ReferenceEquals(left, null))
            return right!.m_isDestroyed;
        if (ReferenceEquals(right, null))
            return left.m_isDestroyed;
        return ReferenceEquals(left, right);
    }

    /// <summary>
    /// Compares engine objects using reference identity and destroyed-object null semantics.
    /// </summary>
    /// <param name="left">
    /// Left operand.
    /// </param>
    /// <param name="right">
    /// Right operand.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operands do not represent the same object.
    /// </returns>
    public static bool operator !=(EngineObject? left, EngineObject? right) => !(left == right);

    /// <summary>
    /// Compares this instance to another object using reference identity.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="obj"/> is this instance.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    /// <summary>
    /// Returns a stable runtime reference hash code.
    /// </summary>
    /// <returns>
    /// The reference hash code.
    /// </returns>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    internal void RegisterIdentity(Guid? persistentId = null)
    {
        if (m_isDestroyed)
            throw new InvalidOperationException($"Destroyed object '{GetType().FullName}' cannot be registered.");
        if (!IdentityAllocator.current.Register(this, persistentId))
            throw new InvalidOperationException($"Identity registration failed for '{GetType().FullName}'.");
    }

    internal void MarkDestroyed()
    {
        if (m_isDestroyed)
            return;
        try
        {
            IdentityAllocator.current.Unregister(this);
        }
        finally
        {
            m_isDestroyed = true;
        }
    }

    internal Guid ReleaseIdentityForReplacement()
    {
        if (m_isDestroyed)
            throw new InvalidOperationException($"Destroyed object '{GetType().FullName}' cannot be replaced.");
        Guid persistentId = identity.persistentId;
        if (!IdentityAllocator.current.Unregister(this))
            throw new InvalidOperationException($"Identity release failed for '{GetType().FullName}'.");
        return persistentId;
    }
}
