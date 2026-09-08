using System;

namespace Inno.References;

/// <summary>
/// Identifies an open reference-resolution protocol independently of its current implementation.
/// </summary>
public readonly struct ReferenceKindId : IEquatable<ReferenceKindId>
{
    /// <summary>
    /// Creates a stable reference kind identifier.
    /// </summary>
    /// <param name="value">
    /// The non-empty protocol identifier.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty or contains surrounding whitespace.
    /// </exception>
    public ReferenceKindId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A reference kind identifier must be non-empty and trimmed.", nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the stable protocol identifier.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this value contains a usable protocol identifier.
    /// </summary>
    public bool isValid => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Compares two identifiers using ordinal protocol identity.
    /// </summary>
    /// <param name="other">
    /// The identifier to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identifiers are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(ReferenceKindId other) => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <summary>
    /// Determines whether an object contains the same reference kind identifier.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object is an equal identifier; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is ReferenceKindId other && Equals(other);

    /// <summary>
    /// Computes an ordinal hash code for this identifier.
    /// </summary>
    /// <returns>
    /// A hash code consistent with ordinal equality.
    /// </returns>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value ?? string.Empty);

    /// <summary>
    /// Formats the protocol identifier for diagnostics.
    /// </summary>
    /// <returns>
    /// The stable identifier, or <c>&lt;invalid&gt;</c> when empty.
    /// </returns>
    public override string ToString() => isValid ? value : "<invalid>";

    /// <summary>
    /// Determines whether two reference kind identifiers are equal.
    /// </summary>
    /// <param name="left">
    /// The first identifier.
    /// </param>
    /// <param name="right">
    /// The second identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identifiers are equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(ReferenceKindId left, ReferenceKindId right) => left.Equals(right);

    /// <summary>
    /// Determines whether two reference kind identifiers are different.
    /// </summary>
    /// <param name="left">
    /// The first identifier.
    /// </param>
    /// <param name="right">
    /// The second identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identifiers are different; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(ReferenceKindId left, ReferenceKindId right) => !left.Equals(right);
}
