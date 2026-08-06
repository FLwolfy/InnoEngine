using Inno.Core.ECS;

namespace Inno.Engine.Scene.Components;

/// <summary>
/// Stores self and hierarchy activation state for an entity.
/// </summary>
internal sealed class ActiveState : Component
{
    /// <summary>
    /// Gets or sets whether the entity is explicitly active.
    /// </summary>
    public bool selfActive { get; set; } = true;

    /// <summary>
    /// Gets whether the entity is active after hierarchy rules are applied.
    /// </summary>
    public bool activeInHierarchy { get; internal set; } = true;

    /// <inheritdoc />
    public override void Reset()
    {
        selfActive = true;
        activeInHierarchy = true;
    }
}
