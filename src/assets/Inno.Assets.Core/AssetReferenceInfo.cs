using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>
/// Provides a stable diagnostic snapshot of references known to the asset pipeline.
/// </summary>
/// <remarks>
/// The known reference count is not the number of CLR strong references and is never used
/// to decide whether an asset can be collected.
/// </remarks>
public sealed class AssetReferenceInfo
{
    internal AssetReferenceInfo(
        Guid persistentId,
        string sourcePath,
        long contentVersion,
        bool isLoaded,
        bool? lastSweepReachability,
        IReadOnlyList<AssetReferenceLocation> references)
    {
        this.persistentId = persistentId;
        this.sourcePath = sourcePath ?? string.Empty;
        this.contentVersion = contentVersion;
        this.isLoaded = isLoaded;
        this.lastSweepReachability = lastSweepReachability;
        this.references = references ?? Array.Empty<AssetReferenceLocation>();
    }

    /// <summary>Gets the persistent asset identity.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the current source-relative path.</summary>
    public string sourcePath { get; }

    /// <summary>Gets the current runtime content version.</summary>
    public long contentVersion { get; }

    /// <summary>Gets whether the asset is currently held by the loader cache.</summary>
    public bool isLoaded { get; }

    /// <summary>
    /// Gets whether an external managed reference was found by the previous unused-asset sweep,
    /// or <see langword="null"/> when no sweep has inspected this asset.
    /// </summary>
    public bool? lastSweepReachability { get; }

    /// <summary>Gets the number of engine-known reference locations.</summary>
    public int knownReferenceCount => references.Count;

    /// <summary>Gets the engine-known reference locations.</summary>
    public IReadOnlyList<AssetReferenceLocation> references { get; }
}
