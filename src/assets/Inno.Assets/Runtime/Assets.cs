using System;

namespace Inno.Assets;

/// <summary>
/// Provides script-facing asset queries through the lookup bound to the current runtime session.
/// </summary>
/// <remarks>
/// This façade owns no asset state. Engine and Editor infrastructure should depend on an explicit
/// <see cref="IAssetLookup"/> instance.
/// </remarks>
public static class Assets
{
    /// <summary>
    /// Loads the canonical asset at a logical catalog path in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="path">
    /// The mount-qualified logical catalog path.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by the current lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active, the asset does not exist, or its type is incompatible.
    /// </exception>
    public static TAsset Load<TAsset>(AssetPath path)
        where TAsset : AssetObject
        => AssetExecutionContext.current.Load<TAsset>(path);

    /// <summary>
    /// Loads the canonical asset with a persistent identity in the current session.
    /// </summary>
    /// <typeparam name="TAsset">
    /// The required asset contract.
    /// </typeparam>
    /// <param name="persistentId">
    /// The non-empty persistent asset identity.
    /// </param>
    /// <returns>
    /// The canonical compatible asset owned by the current lookup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active, the asset does not exist, or its type is incompatible.
    /// </exception>
    public static TAsset Load<TAsset>(Guid persistentId)
        where TAsset : AssetObject
        => AssetExecutionContext.current.Load<TAsset>(persistentId);

    /// <summary>
    /// Tries to load the canonical asset at a logical catalog path in the current session.
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
    /// <see langword="true"/> when the current lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static bool TryLoad<TAsset>(AssetPath path, out TAsset? asset)
        where TAsset : AssetObject
        => AssetExecutionContext.current.TryLoad(path, out asset);

    /// <summary>
    /// Tries to load the canonical asset with a persistent identity in the current session.
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
    /// <see langword="true"/> when the current lookup contains a compatible asset; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset)
        where TAsset : AssetObject
        => AssetExecutionContext.current.TryLoad(persistentId, out asset);
}
