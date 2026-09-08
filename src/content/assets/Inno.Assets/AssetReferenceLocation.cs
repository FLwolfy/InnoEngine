using System;

namespace Inno.Assets;

/// <summary>
/// Describes one engine-known reference location for an asset.
/// </summary>
public sealed class AssetReferenceLocation
{
    /// <summary>
    /// Describes a neutral, persistent location without retaining its live owner.
    /// </summary>
    /// <param name="kind">
    /// The category of reference owner.
    /// </param>
    /// <param name="ownerId">
    /// The owner's persistent identity.
    /// </param>
    /// <param name="ownerName">
    /// The display name captured for diagnostics.
    /// </param>
    /// <param name="propertyPath">
    /// The serialized property or subsystem-relative location.
    /// </param>
    public AssetReferenceLocation(
        AssetReferenceKind kind,
        Guid ownerId,
        string ownerName,
        string propertyPath)
    {
        this.kind = kind;
        this.ownerId = ownerId;
        this.ownerName = ownerName ?? string.Empty;
        this.propertyPath = propertyPath ?? string.Empty;
    }

    /// <summary>
    /// Gets the category of the known reference.
    /// </summary>
    public AssetReferenceKind kind { get; }

    /// <summary>
    /// Gets the persistent identity of the known owner, when available.
    /// </summary>
    public Guid ownerId { get; }

    /// <summary>
    /// Gets the display name of the known owner.
    /// </summary>
    public string ownerName { get; }

    /// <summary>
    /// Gets the serialized or subsystem-relative property path.
    /// </summary>
    public string propertyPath { get; }
}
