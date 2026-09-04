namespace Inno.Extensibility.Modules;

/// <summary>
/// Builds transactional derived state for an assembly catalog generation.
/// </summary>
public interface IAssemblyCatalogParticipant
{
    /// <summary>
    /// Validates a candidate catalog and prepares state without publishing it.
    /// </summary>
    /// <param name="catalog">
    /// The complete candidate assembly catalog.
    /// </param>
    /// <returns>
    /// A transaction controlling publication, completion, and rollback.
    /// </returns>
    IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog);
}
