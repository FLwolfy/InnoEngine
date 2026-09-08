using System;
using Inno.Core.Identity;

namespace Inno.References;

/// <summary>
/// Reports how one descriptor resolves in an immutable reference-catalog generation.
/// </summary>
public sealed class ReferenceResolution
{
    /// <summary>
    /// Creates a reference-resolution result.
    /// </summary>
    /// <param name="descriptor">
    /// The persistent descriptor that was resolved.
    /// </param>
    /// <param name="state">
    /// The resulting state.
    /// </param>
    /// <param name="runtimeIdentity">
    /// The domain-qualified live target identity when resolved.
    /// </param>
    /// <param name="diagnostic">
    /// Optional caller-facing detail for non-resolved states.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="descriptor"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when resolved state and runtime identity are inconsistent.
    /// </exception>
    public ReferenceResolution(
        ReferenceDescriptor descriptor,
        ReferenceResolutionState state,
        RuntimeIdentity? runtimeIdentity = null,
        string? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if ((state == ReferenceResolutionState.Resolved) != runtimeIdentity.HasValue)
            throw new ArgumentException("Only a resolved reference can carry a runtime identity.", nameof(runtimeIdentity));
        this.descriptor = descriptor;
        this.state = state;
        this.runtimeIdentity = runtimeIdentity;
        this.diagnostic = diagnostic;
    }

    /// <summary>
    /// Gets the persistent descriptor that produced this result.
    /// </summary>
    public ReferenceDescriptor descriptor { get; }

    /// <summary>
    /// Gets the current resolution state.
    /// </summary>
    public ReferenceResolutionState state { get; }

    /// <summary>
    /// Gets the live target identity when <see cref="state"/> is <see cref="ReferenceResolutionState.Resolved"/>.
    /// </summary>
    public RuntimeIdentity? runtimeIdentity { get; }

    /// <summary>
    /// Gets optional diagnostics describing why the reference is not resolved.
    /// </summary>
    public string? diagnostic { get; }
}
