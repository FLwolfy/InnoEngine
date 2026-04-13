namespace Inno.Core.ECS;

/// <summary>
/// Base class for ECS components stored in a <see cref="World"/>.
/// </summary>
public abstract class Component
{
    /// <summary>
    /// Gets the owning entity id assigned by the world runtime.
    /// </summary>
    public int entityId { get; internal set; }

    /// <summary>
    /// Gets or sets whether this component is enabled for system processing.
    /// </summary>
    public bool enabled { get; set; } = true;

    /// <summary>
    /// Resets component state before the instance is removed from the world.
    /// </summary>
    public virtual void Reset() { }
}
