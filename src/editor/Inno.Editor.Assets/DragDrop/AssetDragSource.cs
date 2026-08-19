using Inno.Editor.Assets.DragDrop;

using System;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Identifies a dragged asset without loading its runtime object.</summary>
public sealed record AssetDragSource
{
    /// <summary>Creates an asset drag source.</summary>
    public AssetDragSource(Guid persistentId, string relativePath, Type? assetType)
    {
        this.persistentId = persistentId;
        this.relativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        this.assetType = assetType;
    }

    /// <summary>Gets the persistent asset identity.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the source-relative asset path.</summary>
    public string relativePath { get; }

    /// <summary>Gets the imported asset type when one is available.</summary>
    public Type? assetType { get; }
}
