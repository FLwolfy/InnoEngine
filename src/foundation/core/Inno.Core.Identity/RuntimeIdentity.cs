using System;

namespace Inno.Core.Identity;

/// <summary>
/// Addresses a live identity object within one explicitly identified runtime domain.
/// </summary>
/// <remarks>
/// This value is suitable for transient interaction payloads only and must never be persisted.
/// </remarks>
public readonly struct RuntimeIdentity : IEquatable<RuntimeIdentity>
{
    /// <summary>
    /// Creates a runtime identity from its domain and generation-safe object identifier.
    /// </summary>
    /// <param name="domainId">
    /// The identity domain that owns the runtime identifier.
    /// </param>
    /// <param name="runtimeId">
    /// The generation-safe runtime identifier allocated inside the domain.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when either value is invalid.
    /// </exception>
    public RuntimeIdentity(IdentityDomainId domainId, int runtimeId)
    {
        if (!domainId.isValid)
            throw new ArgumentException("A runtime identity requires a valid domain.", nameof(domainId));
        if (runtimeId <= 0)
            throw new ArgumentException("A runtime identity requires a positive runtime identifier.", nameof(runtimeId));
        this.domainId = domainId;
        this.runtimeId = runtimeId;
    }

    /// <summary>
    /// Gets the identity domain that owns this runtime identifier.
    /// </summary>
    public IdentityDomainId domainId { get; }

    /// <summary>
    /// Gets the generation-safe identifier interpreted inside <see cref="domainId"/>.
    /// </summary>
    public int runtimeId { get; }

    /// <summary>
    /// Determines whether this value and another value address the same live identity slot.
    /// </summary>
    /// <param name="other">
    /// The runtime identity to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both the domain and runtime identifier are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(RuntimeIdentity other) => domainId == other.domainId && runtimeId == other.runtimeId;

    /// <summary>
    /// Determines whether an object addresses the same live identity slot.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object is an equal runtime identity; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is RuntimeIdentity other && Equals(other);

    /// <summary>
    /// Computes a hash code from the domain and runtime identifier.
    /// </summary>
    /// <returns>
    /// A hash code consistent with runtime identity equality.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(domainId, runtimeId);

    /// <summary>
    /// Formats this transient identity for diagnostics.
    /// </summary>
    /// <returns>
    /// A domain-qualified runtime identifier.
    /// </returns>
    public override string ToString() => $"{domainId}:{runtimeId}";

    /// <summary>
    /// Determines whether two runtime identities are equal.
    /// </summary>
    /// <param name="left">
    /// The first runtime identity.
    /// </param>
    /// <param name="right">
    /// The second runtime identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(RuntimeIdentity left, RuntimeIdentity right) => left.Equals(right);

    /// <summary>
    /// Determines whether two runtime identities are different.
    /// </summary>
    /// <param name="left">
    /// The first runtime identity.
    /// </param>
    /// <param name="right">
    /// The second runtime identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the values are different; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(RuntimeIdentity left, RuntimeIdentity right) => !left.Equals(right);
}
