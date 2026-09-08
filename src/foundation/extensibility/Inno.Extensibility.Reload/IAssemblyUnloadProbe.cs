namespace Inno.Extensibility.Reload;

/// <summary>
/// Observes one retired collectible generation without retaining its load context.
/// </summary>
public interface IAssemblyUnloadProbe
{
    /// <summary>
    /// Gets a stable diagnostic description of the retired generation.
    /// </summary>
    string description { get; }

    /// <summary>
    /// Gets whether the retired generation is unreachable and its generation storage is released.
    /// </summary>
    bool isCompleted { get; }
}
