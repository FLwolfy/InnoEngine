using System;
using System.Runtime.CompilerServices;

namespace Inno.Core.Identity;

/// <summary>
/// Contract for objects that can be registered into an <see cref="IdentityRegistry"/>.
/// </summary>
public interface IIdentityObject
{
    private sealed class IdentityBox { public Identity value; }
    private static readonly ConditionalWeakTable<object, IdentityBox> IDENTITY_BY_OBJECT = new();

    /// <summary>
    /// Gets this object's identity snapshot.
    /// If identity has never been assigned for this instance, this method will first initialize it
    /// with a new persistent id (via <see cref="Guid.NewGuid"/>), store it, and then return it.
    /// </summary>
    /// <returns>The copied Identity value.</returns>
    public Identity GetIdentity()
    {
        if (IDENTITY_BY_OBJECT.TryGetValue(this, out IdentityBox? identity))
        {
            return identity.value;
        }

        Identity created = new Identity(Guid.NewGuid());
        SetIdentity(created);
        return created;
    }

    /// <summary>
    /// Sets or replaces this instance's identity value in the internal identity store.
    /// </summary>
    /// <param name="value">Identity value to associate with this instance.</param>
    protected internal void SetIdentity(Identity value)
    {
        IdentityBox box = IDENTITY_BY_OBJECT.GetOrCreateValue(this);
        box.value = value;
    }
}
