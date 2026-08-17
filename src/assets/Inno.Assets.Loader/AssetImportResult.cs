using System;

using Inno.Assets.Core;

namespace Inno.Assets.Loader;

/// <summary>
/// Contains the strongly typed output of one asset import operation.
/// </summary>
/// <typeparam name="TAsset">The concrete imported asset type.</typeparam>
public readonly struct AssetImportResult<TAsset> where TAsset : AssetObject
{
    /// <summary>Creates an asset import result.</summary>
    /// <param name="asset">The imported managed asset state.</param>
    /// <param name="runtimePayload">The runtime artifact payload.</param>
    public AssetImportResult(TAsset asset, ReadOnlyMemory<byte> runtimePayload)
    {
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        this.runtimePayload = runtimePayload;
    }

    /// <summary>Gets the imported managed asset state.</summary>
    public TAsset asset { get; }

    /// <summary>Gets the runtime artifact payload.</summary>
    public ReadOnlyMemory<byte> runtimePayload { get; }
}
