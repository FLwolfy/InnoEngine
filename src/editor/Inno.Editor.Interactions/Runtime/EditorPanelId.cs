using System;

namespace Inno.Editor.Interactions;

/// <summary>Identifies one stable editor panel using ordinal string semantics.</summary>
public readonly struct EditorPanelId : IEquatable<EditorPanelId>
{
    /// <summary>Creates a validated stable panel identifier.</summary>
    /// <param name="value">The non-empty stable identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public EditorPanelId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the stable string value.</summary>
    public string value { get; }

    /// <inheritdoc />
    public bool Equals(EditorPanelId other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EditorPanelId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => value ?? string.Empty;

    /// <summary>Determines whether two panel identifiers are equal.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers are equal.</returns>
    public static bool operator ==(EditorPanelId left, EditorPanelId right) => left.Equals(right);

    /// <summary>Determines whether two panel identifiers differ.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers differ.</returns>
    public static bool operator !=(EditorPanelId left, EditorPanelId right) => !left.Equals(right);
}
