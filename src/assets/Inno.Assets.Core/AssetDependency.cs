using System;

namespace Inno.Assets.Core;

/// <summary>
/// Identifies one persistent asset dependency independently of its current source path.
/// </summary>
public struct AssetDependency : IEquatable<AssetDependency>
{
    /// <summary>
    /// Gets or sets the persistent identity of the referenced asset.
    /// </summary>
    public Guid persistentId { get; set; }

    /// <summary>
    /// Gets or sets the stable type identity of the referenced asset.
    /// </summary>
    public Guid stableTypeId { get; set; }

    /// <summary>
    /// Gets or sets the last known source path relative to the asset root.
    /// </summary>
    public string lastKnownPath { get; set; }

    /// <summary>
    /// Creates an asset dependency descriptor.
    /// </summary>
    /// <param name="persistentId">Persistent asset identity.</param>
    /// <param name="stableTypeId">Stable asset type identity.</param>
    /// <param name="lastKnownPath">Last known source path relative to the asset root.</param>
    public AssetDependency(Guid persistentId, Guid stableTypeId, string lastKnownPath)
    {
        this.persistentId = persistentId;
        this.stableTypeId = stableTypeId;
        this.lastKnownPath = lastKnownPath ?? string.Empty;
    }

    /// <summary>
    /// Compares two descriptors by persistent identity.
    /// </summary>
    /// <param name="other">The descriptor to compare.</param>
    /// <returns><see langword="true"/> when both descriptors identify the same asset.</returns>
    public readonly bool Equals(AssetDependency other) => persistentId == other.persistentId;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is AssetDependency other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => persistentId.GetHashCode();
}
