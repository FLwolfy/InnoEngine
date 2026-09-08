using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Assets;

/// <summary>
/// Provides a stable diagnostic snapshot of references known to the asset pipeline.
/// </summary>
/// <remarks>
/// The known reference count is not the number of CLR strong references and is never used
/// to decide whether an asset can be collected.
/// </remarks>
public sealed class AssetReferenceInfo
{
    /// <summary>
    /// Freezes one diagnostic view of the engine-known references to an asset.
    /// </summary>
    /// <param name="persistentId">
    /// The persistent asset identity.
    /// </param>
    /// <param name="assetPath">
    /// The mount-qualified source location.
    /// </param>
    /// <param name="contentVersion">
    /// The observed content revision.
    /// </param>
    /// <param name="isLoaded">
    /// Whether a canonical instance is resident.
    /// </param>
    /// <param name="lastSweepReachability">
    /// The previous reachability result, or null if not inspected.
    /// </param>
    /// <param name="references">
    /// Reference locations copied into this immutable snapshot.
    /// </param>
    public AssetReferenceInfo(
        Guid persistentId,
        AssetPath assetPath,
        long contentVersion,
        bool isLoaded,
        bool? lastSweepReachability,
        IReadOnlyList<AssetReferenceLocation> references)
    {
        this.persistentId = persistentId;
        this.assetPath = assetPath;
        this.contentVersion = contentVersion;
        this.isLoaded = isLoaded;
        this.lastSweepReachability = lastSweepReachability;
        ArgumentNullException.ThrowIfNull(references);
        this.references = Array.AsReadOnly(references.ToArray());
    }

    /// <summary>
    /// Gets the persistent asset identity.
    /// </summary>
    public Guid persistentId { get; }

    /// <summary>
    /// Gets the current mount-qualified asset path.
    /// </summary>
    public AssetPath assetPath { get; }

    /// <summary>
    /// Gets the current runtime content version.
    /// </summary>
    public long contentVersion { get; }

    /// <summary>
    /// Gets whether the asset is currently held by the loader cache.
    /// </summary>
    public bool isLoaded { get; }

    /// <summary>
    /// Gets whether an external managed reference was found by the previous unused-asset sweep,
    /// or <see langword="null"/> when no sweep has inspected this asset.
    /// </summary>
    public bool? lastSweepReachability { get; }

    /// <summary>
    /// Gets the number of engine-known reference locations.
    /// </summary>
    public int knownReferenceCount => references.Count;

    /// <summary>
    /// Gets the engine-known reference locations.
    /// </summary>
    public IReadOnlyList<AssetReferenceLocation> references { get; }
}
