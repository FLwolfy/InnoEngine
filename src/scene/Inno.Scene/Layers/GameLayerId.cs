using System;

namespace Inno.Scene.Layers;

/// <summary>
/// Identifies one logical game layer independently from its compact runtime slot.
/// </summary>
public readonly record struct GameLayerId
{
    /// <summary>
    /// Gets the immutable built-in default layer identity.
    /// </summary>
    public static GameLayerId defaultLayer { get; } = new("inno.default");

    /// <summary>
    /// Creates a globally stable lowercase layer identity.
    /// </summary>
    /// <param name="value">
    /// A namespaced identifier containing lowercase letters, digits, dots, hyphens, or underscores.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty or not portable.
    /// </exception>
    public GameLayerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > 128)
            throw new ArgumentException("A GameLayer ID cannot exceed 128 characters.", nameof(value));
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            bool valid = character is >= 'a' and <= 'z'
                         || character is >= '0' and <= '9'
                         || character is '.' or '-' or '_';
            if (!valid)
            {
                throw new ArgumentException(
                    "A GameLayer ID may contain only lowercase ASCII letters, digits, dots, hyphens, and underscores.",
                    nameof(value));
            }
        }
        if (normalized[0] is '.' or '-' or '_' || normalized[^1] is '.' or '-' or '_')
            throw new ArgumentException("A GameLayer ID must begin and end with a letter or digit.", nameof(value));
        if (!normalized.Contains('.', StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A GameLayer ID must contain at least two non-empty namespace segments.",
                nameof(value));
        }
        this.value = normalized;
    }

    /// <summary>
    /// Gets the globally stable identity string.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this value contains a usable identity.
    /// </summary>
    public bool isValid => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
