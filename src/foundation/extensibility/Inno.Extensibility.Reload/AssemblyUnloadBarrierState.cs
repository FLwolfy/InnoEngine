namespace Inno.Extensibility.Reload;

/// <summary>
/// Describes the terminal or waiting state of an assembly unload barrier.
/// </summary>
public enum AssemblyUnloadBarrierState
{
    /// <summary>
    /// At least one retired collectible generation remains reachable.
    /// </summary>
    AwaitingCollection,

    /// <summary>
    /// Every retired generation is unreachable and cleaned up.
    /// </summary>
    Completed,

    /// <summary>
    /// The retention threshold was reached and the owning reload subsystem must stop accepting work.
    /// </summary>
    Faulted
}
