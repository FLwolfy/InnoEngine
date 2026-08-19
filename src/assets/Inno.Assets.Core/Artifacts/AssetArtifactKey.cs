using System;

namespace Inno.Assets.Core;

/// <summary>Identifies one immutable content-addressed artifact bundle.</summary>
public readonly struct AssetArtifactKey : IEquatable<AssetArtifactKey>
{
    /// <summary>Creates an artifact key from a hexadecimal content fingerprint.</summary>
    /// <param name="value">The hexadecimal content fingerprint.</param>
    public AssetArtifactKey(string value)
    {
        this.value = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    /// <summary>Gets an empty artifact key.</summary>
    public static AssetArtifactKey empty => default;

    /// <summary>Gets the normalized hexadecimal value.</summary>
    public string value { get; } = string.Empty;

    /// <summary>Gets whether the key is empty.</summary>
    public bool isEmpty => string.IsNullOrEmpty(value);

    /// <inheritdoc />
    public bool Equals(AssetArtifactKey other)
        => string.Equals(value, other.value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is AssetArtifactKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => value ?? string.Empty;

    /// <summary>Determines whether two artifact keys are equal.</summary>
    public static bool operator ==(AssetArtifactKey left, AssetArtifactKey right) => left.Equals(right);

    /// <summary>Determines whether two artifact keys differ.</summary>
    public static bool operator !=(AssetArtifactKey left, AssetArtifactKey right) => !left.Equals(right);
}
