namespace Inno.Core.ECS;

/// <summary>
/// Base class for world systems.
/// </summary>
public abstract class System
{
    /// <summary>
    /// Gets execution order. Lower values run first.
    /// </summary>
    public virtual int order => 0;

    /// <summary>
    /// Processes the fixed timestep stage.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="fixedDeltaTime">Fixed timestep delta in seconds.</param>
    public virtual void FixedProcess(World world, float fixedDeltaTime)
    {
    }

    /// <summary>
    /// Processes the variable timestep stage.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public virtual void Process(World world, float deltaTime)
    {
    }

    /// <summary>
    /// Processes the late variable timestep stage.
    /// </summary>
    /// <param name="world">Target world.</param>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public virtual void LateProcess(World world, float deltaTime)
    {
    }
}
