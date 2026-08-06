namespace Inno.Core.ECS;

using Inno.Core.Identity;

/// <summary>
/// Base class for ECS components stored in a <see cref="World"/>.
/// </summary>
public abstract class Component : IIdentityObject
{
    /// <summary>
    /// Gets the owning entity id assigned by the world runtime.
    /// </summary>
    internal int entityId { get; set; }

    /// <summary>
    /// Gets this component's identity.
    /// </summary>
    public Identity identity => ((IIdentityObject)this).GetIdentity();

    /// <summary>
    /// Resets component state before the instance is removed from the world.
    /// </summary>
    public abstract void Reset();
}
