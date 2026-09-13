using System;
using Inno.Core.Execution;

namespace Inno.Assets;

/// <summary>
/// Grants one database exclusive mutation authority over the asset instances it initializes.
/// </summary>
/// <remarks>
/// This capability is not a global mutation bridge and is not exported to scripts. A database keeps
/// its owner private and explicitly releases canonical assets; the capability itself retains no assets.
/// </remarks>
public sealed class AssetRuntimeOwner
{
    private readonly object m_authority = new();
    private readonly WeakReference<IAssetPropertyStateResolver>? m_properties;

    /// <summary>Creates exclusive mutation authority with optional owner-bound extension state restoration.</summary>
    /// <param name="properties">Owner resolver, weakly referenced so stale assets cannot retain a retired database.</param>
    public AssetRuntimeOwner(IAssetPropertyStateResolver? properties = null)
        => m_properties = properties is null ? null : new(properties);

    /// <summary>
    /// Reads the source fingerprint of an asset claimed by this owner.
    /// </summary>
    /// <param name="asset">
    /// The owned asset.
    /// </param>
    /// <returns>
    /// The fingerprint currently committed to the asset.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The asset belongs to a different owner.
    /// </exception>
    public string GetSourceHash(AssetObject asset)
    {
        Validate(asset);
        return asset.sourceHash;
    }

    /// <summary>
    /// Claims an unowned asset or commits new runtime state to an already owned instance.
    /// </summary>
    /// <param name="asset">
    /// The instance to initialize; ownership cannot be transferred between databases.
    /// </param>
    /// <param name="assetPath">
    /// The current mount-qualified source path.
    /// </param>
    /// <param name="sourceHash">
    /// The immutable source fingerprint.
    /// </param>
    /// <param name="payload">
    /// Runtime bytes copied before publication.
    /// </param>
    /// <param name="isMissing">
    /// Whether the preserved reference is currently unavailable.
    /// </param>
    /// <param name="version">
    /// The current content revision used to invalidate runtime caches.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The asset belongs to a different owner.
    /// </exception>
    public void Initialize(AssetObject asset, AssetPath assetPath, string sourceHash,
        ReadOnlyMemory<byte> payload, bool isMissing, long version)
    {
        Validate(asset);
        asset.InitializeRuntimeState(assetPath, sourceHash, payload, isMissing, version);
    }

    /// <summary>
    /// Updates source location without replacing committed runtime content.
    /// </summary>
    /// <param name="asset">
    /// The owned canonical instance.
    /// </param>
    /// <param name="assetPath">
    /// The new mount-qualified location.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The asset belongs to a different owner.
    /// </exception>
    public void UpdateAssetPath(AssetObject asset, AssetPath assetPath)
    {
        Validate(asset);
        asset.UpdateAssetPath(assetPath);
    }

    /// <summary>
    /// Releases an owned instance after quiescence, without relinquishing its mutation authority.
    /// </summary>
    /// <param name="asset">
    /// The canonical instance or uncommitted candidate to retire.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The asset belongs to a different owner.
    /// </exception>
    /// <exception cref="RetirementPendingException">
    /// Owned work remains active. Retain the instance and retry at the owner's retirement safe point.
    /// </exception>
    public void Release(AssetObject asset)
    {
        Validate(asset);
        asset.ReleaseRuntimeResources();
    }

    private void Validate(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.ClaimRuntimeOwner(m_authority, m_properties);
    }
}
