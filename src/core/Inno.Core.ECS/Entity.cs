namespace Inno.Core.ECS;

using Inno.Core.Identity;

/// <summary>
/// Base ECS entity stored in a world.
/// </summary>
public abstract class Entity : IIdentityObject
{
    /// <summary>
    /// Gets this entity's identity.
    /// </summary>
    public Identity identity => ((IIdentityObject)this).GetIdentity();
}
