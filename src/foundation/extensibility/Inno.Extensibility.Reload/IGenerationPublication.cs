namespace Inno.Extensibility.Reload;

/// <summary>
/// Publishes a candidate and supplies a weak retirement monitor at the irreversible commit boundary.
/// </summary>
/// <typeparam name="TProbe">
/// The non-owning probe used to verify retired collectible contexts.
/// </typeparam>
public interface IGenerationPublication<out TProbe> where TProbe : IAssemblyUnloadProbe
{
    /// <summary>
    /// Makes the fully prepared candidate provisionally visible at an owner-thread safe point.
    /// </summary>
    void Activate();
    /// <summary>
    /// Restores the previous publication and retires discarded candidates.
    /// </summary>
    void Rollback();
    /// <summary>
    /// Commits publication after all dependent changes apply successfully.
    /// </summary>
    /// <returns>
    /// A weak probe observing retired contexts; success is not reported until it completes.
    /// </returns>
    TProbe Complete();
}
