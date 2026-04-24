using System;

namespace Inno.Assets.Core;

/// <summary>
/// Lightweight reference to an asset instance.
/// </summary>
/// <typeparam name="TAsset">Asset type.</typeparam>
public readonly struct AssetHandle<TAsset> where TAsset : AssetObject
{
    /// <summary>
    /// Stable persistent asset id.
    /// </summary>
    public Guid persistentId { get; }
    /// <summary>
    /// Runtime id in current process.
    /// </summary>
    public int runtimeId { get; }

    /// <summary>
    /// Returns true when <see cref="persistentId"/> is non-empty.
    /// </summary>
    public bool isValid => persistentId != Guid.Empty;

    internal AssetHandle(Guid persistentId, int runtimeId)
    {
        this.persistentId = persistentId;
        this.runtimeId = runtimeId;
    }

    /// <summary>
    /// Returns a human-readable handle representation.
    /// </summary>
    /// <returns>Type + ids representation.</returns>
    public override string ToString()
    {
        if (!isValid)
            return $"{typeof(TAsset).Name} (Invalid)";

        return $"{typeof(TAsset).Name} [{persistentId}] (runtime: {runtimeId})";
    }
}
