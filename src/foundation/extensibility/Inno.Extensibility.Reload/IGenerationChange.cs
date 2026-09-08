namespace Inno.Extensibility.Reload;

/// <summary>
/// Stages domain-owned state around an atomic generation publication without retaining it after completion.
/// </summary>
public interface IGenerationChange
{
    /// <summary>
    /// Quiesces old instances while the old publication remains active.
    /// </summary>
    void PrepareForActivation();
    /// <summary>
    /// Applies captured state after candidate publication; partial changes must be rollback-safe.
    /// </summary>
    void Apply();
    /// <summary>
    /// Releases old instances after irreversible publication; cleanup failures fault the owner.
    /// </summary>
    void Complete();
    /// <summary>
    /// Removes provisional structures before restoring the old publication.
    /// </summary>
    void RollbackStructure();
    /// <summary>
    /// Restores old values and lifecycle after the previous publication has been restored.
    /// </summary>
    void RestorePreviousState();
}
