namespace Inno.Core.Identity;

/// <summary>
/// Contract for objects that can be registered into an <see cref="IdentityRegistry"/>.
/// </summary>
public abstract class IdentityObject
{
    /// <summary>
    /// Gets this object's identity payload.
    /// Setter is protected so only derived types and internal registry flows can update it.
    /// </summary>
    public Identity identity { get; protected set; }

    internal void SetIdentity(Identity value)
    {
        identity = value;
    }
}
