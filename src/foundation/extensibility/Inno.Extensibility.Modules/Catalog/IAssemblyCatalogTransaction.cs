namespace Inno.Extensibility.Modules;

/// <summary>
/// Controls one participant's prepared state during an assembly catalog transaction.
/// </summary>
public interface IAssemblyCatalogTransaction
{
    /// <summary>
    /// Gets an optional short-lived context exposed through <see cref="AssemblyReloadContext"/>.
    /// </summary>
    object? context { get; }

    /// <summary>
    /// Publishes the prepared candidate state.
    /// </summary>
    void Activate();

    /// <summary>
    /// Finalizes an activated state and releases the previous state without performing further publication work.
    /// </summary>
    /// <remarks>
    /// Ordinary cleanup failures must remain observable after all independent owners have been attempted.
    /// The coordinator faults further generation admission instead of ignoring the failure or pretending publication can roll back.
    /// Unfinished retirement must retain its owners and propagate the retirement barrier before releasing their dependencies.
    /// </remarks>
    void Complete();

    /// <summary>
    /// Restores the previous state and releases the candidate state.
    /// </summary>
    /// <remarks>
    /// Implementations should isolate individual cleanup failures so every candidate resource can be released.
    /// </remarks>
    void Rollback();
}
