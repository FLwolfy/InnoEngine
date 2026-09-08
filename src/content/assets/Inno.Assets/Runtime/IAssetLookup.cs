using System;

namespace Inno.Assets;

/// <summary>
/// Defines the read-only asset lookup boundary shared by authoring and deployed runtime databases.
/// </summary>
public interface IAssetLookup
{
    /// <summary>
    /// Loads the canonical asset at a logical catalog path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by this lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the asset does not exist or has an incompatible concrete type.
    /// </exception>
    TAsset Load<TAsset>(AssetPath path)
        where TAsset : AssetObject;

    /// <summary>
    /// Loads the canonical asset with a persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by this lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the asset does not exist or has an incompatible concrete type.
    /// </exception>
    TAsset Load<TAsset>(Guid persistentId)
        where TAsset : AssetObject;

    /// <summary>
    /// Tries to load the canonical asset at a logical catalog path.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    bool TryLoad<TAsset>(AssetPath path, out TAsset? asset)
        where TAsset : AssetObject;

    /// <summary>
    /// Tries to load the canonical asset with a persistent identity.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <param name="asset">
    /// Receives the canonical compatible asset when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset)
        where TAsset : AssetObject;
}
