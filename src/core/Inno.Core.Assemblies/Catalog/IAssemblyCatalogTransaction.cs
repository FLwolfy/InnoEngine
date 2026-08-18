namespace Inno.Core.Assemblies;

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
    /// Finalizes an activated state and releases the previous state.
    /// </summary>
    void Complete();

    /// <summary>
    /// Restores the previous state and releases the candidate state.
    /// </summary>
    void Rollback();
}
