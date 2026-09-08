namespace Inno.References;

/// <summary>
/// Describes the current resolution state without discarding persistent reference intent.
/// </summary>
public enum ReferenceResolutionState
{
    /// <summary>
    /// The slot has no assigned target.
    /// </summary>
    Unassigned,

    /// <summary>
    /// The current generation resolved a compatible live target.
    /// </summary>
    Resolved,

    /// <summary>
    /// The intended target is temporarily unavailable.
    /// </summary>
    Missing,

    /// <summary>
    /// The intended target exists but does not satisfy the stable type constraint.
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// The descriptor or authoritative source is invalid.
    /// </summary>
    Invalid
}
