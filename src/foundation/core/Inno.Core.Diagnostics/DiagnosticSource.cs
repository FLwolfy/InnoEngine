using System;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Identifies one independent producer whose diagnostics replace its previous publication.
/// </summary>
public readonly struct DiagnosticSource : IEquatable<DiagnosticSource>
{
    /// <summary>
    /// Creates an immutable identity for one independently replaceable diagnostic producer.
    /// </summary>
    /// <param name="id">
    /// The globally stable machine-readable producer identifier.
    /// </param>
    /// <param name="displayName">
    /// The non-empty producer name presented by diagnostic consumers.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when either value is empty.
    /// </exception>
    public DiagnosticSource(string id, string displayName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A diagnostic source identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A diagnostic source display name is required.", nameof(displayName));
        this.id = id;
        this.displayName = displayName;
    }

    /// <summary>
    /// Gets the stable machine-readable producer identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the user-facing producer name.
    /// </summary>
    public string displayName { get; }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    public bool Equals(DiagnosticSource other)
        => string.Equals(id, other.id, StringComparison.Ordinal);

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj)
        => obj is DiagnosticSource other && Equals(other);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(id ?? string.Empty);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString()
        => string.IsNullOrWhiteSpace(displayName) ? id ?? string.Empty : displayName;

    /// <summary>
    /// Determines whether two sources have the same stable identifier.
    /// </summary>
    /// <param name="left">
    /// The left source.
    /// </param>
    /// <param name="right">
    /// The right source.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both source identifiers are equal.
    /// </returns>
    public static bool operator ==(DiagnosticSource left, DiagnosticSource right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two sources have different stable identifiers.
    /// </summary>
    /// <param name="left">
    /// The left source.
    /// </param>
    /// <param name="right">
    /// The right source.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source identifiers differ.
    /// </returns>
    public static bool operator !=(DiagnosticSource left, DiagnosticSource right)
        => !left.Equals(right);
}
