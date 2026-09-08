namespace Inno.References;

/// <summary>
/// Describes one candidate missing-state transition at an owner-thread safe point.
/// </summary>
public sealed class ReferenceRecoveryChange
{
    /// <summary>
    /// Creates a recovery change from preserved state and its candidate resolution.
    /// </summary>
    /// <param name="missingState">
    /// The immutable state captured before the candidate generation was applied.
    /// </param>
    /// <param name="resolution">
    /// The result produced by the candidate reference catalog.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when either argument is null.
    /// </exception>
    public ReferenceRecoveryChange(SerializedMissingState missingState, ReferenceResolution resolution)
    {
        System.ArgumentNullException.ThrowIfNull(missingState);
        System.ArgumentNullException.ThrowIfNull(resolution);
        this.missingState = missingState;
        this.resolution = resolution;
    }

    /// <summary>
    /// Gets the preserved owner and target state.
    /// </summary>
    public SerializedMissingState missingState { get; }

    /// <summary>
    /// Gets the candidate generation's resolution for the preserved descriptor.
    /// </summary>
    public ReferenceResolution resolution { get; }
}
