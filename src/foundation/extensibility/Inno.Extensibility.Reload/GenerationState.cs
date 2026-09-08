namespace Inno.Extensibility.Reload;

/// <summary>
/// Describes the shared admission gate for owner-thread generation transactions.
/// </summary>
public enum GenerationState
{
    /// <summary>
    /// No candidate or unverified retirement is pending.
    /// </summary>
    Ready,
    /// <summary>
    /// A candidate is being activated or rolled back.
    /// </summary>
    Transitioning,
    /// <summary>
    /// Publication finished but retired collectible contexts have not been verified unreachable.
    /// </summary>
    AwaitingCollection,
    /// <summary>
    /// A rollback, cleanup or unload failure requires a complete host restart.
    /// </summary>
    Faulted
}
