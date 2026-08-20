using Inno.Editor.Assets.DragDrop;

using System;

namespace Inno.Editor.Assets.DragDrop;

/// <summary>Identifies a dragged asset without loading its runtime object.</summary>
public sealed record AssetDragSource
{
    /// <summary>
    /// Creates a lightweight drag source that identifies an asset without loading its runtime object.
    /// </summary>
    /// <param name="persistentId">The stable Asset Database identity, or an empty value for an untracked source.</param>
    /// <param name="relativePath">The normalized source-relative asset path.</param>
    /// <param name="assetType">The imported runtime asset type when it can be resolved.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="relativePath"/> is <see langword="null"/>.</exception>
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
