namespace Inno.Rendering.Runtime;

/// <summary>
/// Controls one isolated rendering-extension candidate from preparation through atomic completion.
/// </summary>
public interface IRenderRuntimeReloadTransaction
{
    /// <summary>
    /// Builds and validates every candidate registry and last-good rendering generation.
    /// </summary>
    void Prepare();

    /// <summary>
    /// Atomically selects the prepared candidate without retiring the previous generation.
    /// </summary>
    void Activate();

    /// <summary>
    /// Commits an activated candidate and retires the previous generation.
    /// </summary>
    void Complete();

    /// <summary>
    /// Discards the candidate and restores the previous generation after provisional activation.
    /// </summary>
    void Rollback();
}
