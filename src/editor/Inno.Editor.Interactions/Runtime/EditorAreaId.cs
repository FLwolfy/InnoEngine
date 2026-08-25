using System;

namespace Inno.Editor.Interactions;

/// <summary>Identifies one stable editor interaction area using ordinal string semantics.</summary>
public readonly struct EditorAreaId : IEquatable<EditorAreaId>
{
    /// <summary>Creates a validated stable area identifier.</summary>
    /// <param name="value">The non-empty stable identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public EditorAreaId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the stable string value.</summary>
    public string value { get; }

    /// <inheritdoc />
    public bool Equals(EditorAreaId other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EditorAreaId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => value ?? string.Empty;

    /// <summary>Determines whether two area identifiers are equal.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers are equal.</returns>
    public static bool operator ==(EditorAreaId left, EditorAreaId right) => left.Equals(right);

    /// <summary>Determines whether two area identifiers differ.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers differ.</returns>
    public static bool operator !=(EditorAreaId left, EditorAreaId right) => !left.Equals(right);
}
