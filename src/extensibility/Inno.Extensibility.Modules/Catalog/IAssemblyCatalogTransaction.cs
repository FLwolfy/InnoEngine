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
    /// Implementations should isolate cleanup failures internally. The coordinator reports and ignores an
    /// exception from this method because the candidate has already been published and cannot be rolled back safely.
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
