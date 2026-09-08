namespace Inno.Runtime.Contracts;

/// <summary>
/// Creates one runtime subsystem instance for each declared Host or Session owner.
/// </summary>
public interface IRuntimeSubsystemFactory
{
    /// <summary>
    /// Gets stable ordering and dependency metadata without creating runtime state.
    /// </summary>
    RuntimeSubsystemDescriptor descriptor { get; }

    /// <summary>
    /// Creates a subsystem owned exclusively by the supplied construction scope.
    /// </summary>
    /// <param name="context">
    /// Stable foundation services and a private factory lifetime. Register partial allocations and work here before callbacks can fail.
    /// </param>
    /// <returns>
    /// A new unattached runtime subsystem; after returning, its lifecycle belongs to the pipeline.
    /// </returns>
    IRuntimeSubsystem Create(RuntimeSubsystemContext context);
}
