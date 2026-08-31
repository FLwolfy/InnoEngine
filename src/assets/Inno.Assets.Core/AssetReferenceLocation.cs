using System;

namespace Inno.Assets.Core;

/// <summary>
/// Describes one engine-known reference location for an asset.
/// </summary>
public sealed class AssetReferenceLocation
{
    internal AssetReferenceLocation(
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

    /// <summary>Gets the category of the known reference.</summary>
    public AssetReferenceKind kind { get; }

    /// <summary>Gets the persistent identity of the known owner, when available.</summary>
    public Guid ownerId { get; }

    /// <summary>Gets the display name of the known owner.</summary>
    public string ownerName { get; }

    /// <summary>Gets the serialized or subsystem-relative property path.</summary>
    public string propertyPath { get; }
}
