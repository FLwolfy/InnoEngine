using System;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Identifies one independent producer whose diagnostics replace its previous publication.
/// </summary>
public readonly struct DiagnosticSource : IEquatable<DiagnosticSource>
{
    /// <summary>
    /// Creates a diagnostic source.
    /// </summary>
    /// <param name="id">The stable machine-readable identifier used for replacement and clearing.</param>
    /// <param name="displayName">The user-facing producer name displayed by diagnostic tools.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="displayName"/> is empty or contains only whitespace.
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

    /// <inheritdoc />
    public bool Equals(DiagnosticSource other)
        => string.Equals(id, other.id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DiagnosticSource other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(id ?? string.Empty);

    /// <inheritdoc />
    public override string ToString()
        => string.IsNullOrWhiteSpace(displayName) ? id ?? string.Empty : displayName;

    /// <summary>
    /// Determines whether two sources have the same stable identifier.
    /// </summary>
    /// <param name="left">The left source.</param>
    /// <param name="right">The right source.</param>
    /// <returns><see langword="true"/> when both source identifiers are equal.</returns>
    public static bool operator ==(DiagnosticSource left, DiagnosticSource right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two sources have different stable identifiers.
    /// </summary>
    /// <param name="left">The left source.</param>
    /// <param name="right">The right source.</param>
    /// <returns><see langword="true"/> when the source identifiers differ.</returns>
    public static bool operator !=(DiagnosticSource left, DiagnosticSource right)
        => !left.Equals(right);
}
