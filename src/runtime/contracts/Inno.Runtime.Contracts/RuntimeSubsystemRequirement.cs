namespace Inno.Runtime.Contracts;

/// <summary>
/// Selects startup failure behavior without changing runtime-frame or retirement error semantics.
/// </summary>
public enum RuntimeSubsystemRequirement
{
    /// <summary>
    /// Missing capabilities, dependencies or failed startup reject the entire owner.
    /// </summary>
    Required,

    /// <summary>
    /// A fully compensated startup may remain unavailable with diagnostics; cleanup failures still reject the owner.
    /// </summary>
    Optional
}
