namespace Inno.Core.ECS;

/// <summary>
/// Represents an ECS entity handle in a world.
/// </summary>
/// <param name="id">Unique entity id.</param>
public sealed class Entity(int id)
{
    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public int id { get; } = id;
}
