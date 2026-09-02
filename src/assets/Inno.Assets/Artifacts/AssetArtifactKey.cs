using System;

namespace Inno.Assets;

/// <summary>
/// Identifies one immutable content-addressed artifact bundle.
/// </summary>
public readonly struct AssetArtifactKey : IEquatable<AssetArtifactKey>
{
    /// <summary>
    /// Creates an artifact key from a hexadecimal content fingerprint.
    /// </summary>
    /// <param name="value">
    /// The hexadecimal content fingerprint.
    /// </param>
    public AssetArtifactKey(string value)
    {
        this.value = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Gets an empty artifact key.
    /// </summary>
    public static AssetArtifactKey empty => default;

    /// <summary>
    /// Gets the normalized hexadecimal value.
    /// </summary>
    public string value { get; } = string.Empty;

    /// <summary>
    /// Gets whether the key is empty.
    /// </summary>
    public bool isEmpty => string.IsNullOrEmpty(value);

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    public bool Equals(AssetArtifactKey other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

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
        => obj is AssetArtifactKey other && Equals(other);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(value ?? string.Empty);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;

    /// <summary>
    /// Determines whether two artifact keys are equal.
    /// </summary>
    /// <param name="left">
    /// The first artifact key to compare.
    /// </param>
    /// <param name="right">
    /// The second artifact key to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both keys contain the same normalized fingerprint; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(AssetArtifactKey left, AssetArtifactKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two artifact keys differ.
    /// </summary>
    /// <param name="left">
    /// The first artifact key to compare.
    /// </param>
    /// <param name="right">
    /// The second artifact key to compare.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the normalized fingerprints differ; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(AssetArtifactKey left, AssetArtifactKey right) => !left.Equals(right);
}
