using System;
using Inno.Core.Identity;

namespace Inno.Animation;

/// <summary>
/// Identifies a transient animation destination without retaining the destination object.
/// </summary>
public readonly struct AnimationTarget : IEquatable<AnimationTarget>
{
    private readonly Identity m_identity;
    private readonly RuntimeIdentity m_runtimeIdentity;
    private readonly bool m_samplingOnly;

    /// <summary>
    /// Captures one currently registered destination in its original identity domain.
    /// </summary>
    /// <param name="identity">
    /// The destination's live identity, not a detached persistent identifier.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The identity is not registered.
    /// </exception>
    public AnimationTarget(Identity identity)
    {
        m_runtimeIdentity = identity.runtimeIdentity
            ?? throw new ArgumentException("Animation targets require a live identity.", nameof(identity));
        m_identity = identity;
        m_samplingOnly = false;
    }

    private AnimationTarget(bool samplingOnly)
    {
        m_identity = default;
        m_runtimeIdentity = default;
        m_samplingOnly = samplingOnly;
    }

    /// <summary>
    /// Gets an explicit destination for sampling and markers without object binding.
    /// </summary>
    public static AnimationTarget samplingOnly => new(true);

    /// <summary>
    /// Gets whether this target deliberately omits object binding.
    /// </summary>
    public bool isSamplingOnly => m_samplingOnly;

    /// <summary>
    /// Gets whether this value was initialized with a destination or sampling policy.
    /// </summary>
    public bool isValid => m_samplingOnly || m_identity.persistentId != Guid.Empty;

    /// <summary>
    /// Gets the persistent destination identifier, or empty for sampling-only playback.
    /// </summary>
    public Guid persistentId => m_identity.persistentId;

    /// <summary>
    /// Resolves the original runtime slot without binding to a replacement generation.
    /// </summary>
    /// <typeparam name="TObject">
    /// The expected destination contract.
    /// </typeparam>
    /// <returns>
    /// The live destination, or null for a missing, retired, or incompatible target.
    /// </returns>
    public TObject? Resolve<TObject>() where TObject : IdentityObject => m_identity.Resolve<TObject>();

    /// <summary>
    /// Compares runtime destinations, including their identity domain.
    /// </summary>
    /// <param name="other">
    /// The destination to compare.
    /// </param>
    /// <returns>
    /// True when both values denote the same runtime destination or sampling policy.
    /// </returns>
    public bool Equals(AnimationTarget other)
        => m_samplingOnly == other.m_samplingOnly && m_runtimeIdentity.Equals(other.m_runtimeIdentity);

    /// <summary>
    /// Compares a boxed destination with this value.
    /// </summary>
    /// <param name="obj">
    /// The value to compare.
    /// </param>
    /// <returns>
    /// True for an equal animation destination.
    /// </returns>
    public override bool Equals(object? obj) => obj is AnimationTarget other && Equals(other);

    /// <summary>
    /// Hashes the domain-qualified runtime destination.
    /// </summary>
    /// <returns>
    /// A hash consistent with destination equality.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(m_runtimeIdentity, m_samplingOnly);
}
