using System;

namespace Inno.Core.Identity;

/// <summary>
/// Identifies one isolated runtime identity namespace within the current process.
/// </summary>
/// <remarks>
/// Domain identifiers are runtime-only values. They must never be serialized or used as persistent object identity.
/// </remarks>
public readonly struct IdentityDomainId : IEquatable<IdentityDomainId>
{
    internal IdentityDomainId(int value)
    {
        this.value = value;
    }

    /// <summary>
    /// Gets the process-local numeric value assigned to this domain.
    /// </summary>
    public int value { get; }

    /// <summary>
    /// Gets whether this value identifies an allocated runtime domain.
    /// </summary>
    public bool isValid => value > 0;

    /// <summary>
    /// Determines whether this value and another value identify the same runtime domain.
    /// </summary>
    /// <param name="other">
    /// The domain identifier to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values identify the same domain; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(IdentityDomainId other) => value == other.value;

    /// <summary>
    /// Determines whether an object represents the same runtime domain.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object is an equal domain identifier; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is IdentityDomainId other && Equals(other);

    /// <summary>
    /// Computes a hash code for this runtime domain identifier.
    /// </summary>
    /// <returns>
    /// A hash code derived from the process-local domain value.
    /// </returns>
    public override int GetHashCode() => value;

    /// <summary>
    /// Formats this runtime domain identifier for diagnostics.
    /// </summary>
    /// <returns>
    /// The numeric domain value, or <c>&lt;invalid&gt;</c> when the value is not allocated.
    /// </returns>
    public override string ToString() => isValid ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<invalid>";

    /// <summary>
    /// Determines whether two values identify the same runtime domain.
    /// </summary>
    /// <param name="left">
    /// The first domain identifier.
    /// </param>
    /// <param name="right">
    /// The second domain identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(IdentityDomainId left, IdentityDomainId right) => left.Equals(right);

    /// <summary>
    /// Determines whether two values identify different runtime domains.
    /// </summary>
    /// <param name="left">
    /// The first domain identifier.
    /// </param>
    /// <param name="right">
    /// The second domain identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the values are different; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(IdentityDomainId left, IdentityDomainId right) => !left.Equals(right);
}
