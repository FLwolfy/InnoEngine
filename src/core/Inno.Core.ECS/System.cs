namespace Inno.Core.ECS;

/// <summary>
/// Contract for world systems that update each frame.
/// </summary>
public interface ISystem
{
    /// <summary>
    /// Gets execution order. Lower values run first.
    /// </summary>
    int order { get; }

    /// <summary>
    /// Updates this system for a frame.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    void Update(World world, float deltaTime);
}

/// <summary>
/// Convenience base type for systems associated with a component type.
/// </summary>
/// <typeparam name="TComponent">Associated component type.</typeparam>
public abstract class System<TComponent> : ISystem
    where TComponent : Component
{
    /// <inheritdoc />
    public virtual int order => 0;

    /// <summary>
    /// Processes one frame using the provided world.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    protected abstract void Process(World world, float deltaTime);

    /// <inheritdoc />
    public void Update(World world, float deltaTime) => Process(world, deltaTime);
}
