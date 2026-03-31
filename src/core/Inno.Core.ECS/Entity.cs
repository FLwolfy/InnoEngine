using System;

namespace Inno.Core.ECS;

/// <summary>
/// Represents an ECS entity handle in a world.
/// </summary>
/// <param name="id">Unique entity id.</param>
/// <param name="parentGuid">Optional parent entity id.</param>
public sealed class Entity(Guid id, Guid? parentGuid = null)
{
    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public Guid id { get; } = id;

    /// <summary>
    /// Gets the parent entity identifier when present.
    /// </summary>
    public Guid? parentGuid { get; } = parentGuid;
}
