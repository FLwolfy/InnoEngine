namespace Inno.Core.Identity;

/// <summary>
/// Provides instance-owned persistent and session-local identity state managed by an <see cref="IdentityAllocator"/>.
/// </summary>
public abstract class IdentityObject
{
    private Identity m_identity = new(System.Guid.NewGuid());

    /// <summary>
    /// Gets this object's current persistent and optional session-local identity snapshot.
    /// </summary>
    public Identity identity => m_identity;

    internal void SetIdentity(Identity value) => m_identity = value;
}
