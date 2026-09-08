using System.Collections.Generic;
using Inno.Extensibility.Reload;

namespace Inno.References;

/// <summary>
/// Applies domain-owned missing and recovered representations as one candidate transaction participant.
/// </summary>
public interface IReferenceRecoveryParticipant : IGenerationChange
{
    /// <summary>
    /// Validates resolved slots after provisional structures and values have been applied, before commit.
    /// </summary>
    /// <param name="changes">
    /// The complete immutable set of candidate recovery changes; optional missing slots remain preserved.
    /// </param>
    void Validate(IReadOnlyList<ReferenceRecoveryChange> changes);

}
