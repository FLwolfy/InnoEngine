namespace Inno.Core.ECS;

/// <summary>
/// Represents an ECS entity handle in a world.
/// </summary>
/// <param name="id">Unique entity id.</param>
/// <param name="parentId">Optional parent entity id.</param>
public sealed class Entity(int id, int? parentId = null)
{
    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public int id { get; } = id;

    /// <summary>
    /// Gets the parent entity identifier when present.
    /// </summary>
    public int? parentId { get; } = parentId;
}
