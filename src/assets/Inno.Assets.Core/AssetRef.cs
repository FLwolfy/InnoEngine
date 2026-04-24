using System;

using Inno.Core.Identity;

namespace Inno.Assets.Core;

/// <summary>
/// Lightweight reference to an asset instance.
/// </summary>
/// <typeparam name="TAsset">Asset type.</typeparam>
public readonly struct AssetRef<TAsset> where TAsset : AssetObject
{
    public Identity identity { get; }

    /// <summary>
    /// Returns true when identity has non-empty persistent id.
    /// </summary>
    public bool isValid => identity.persistentId != Guid.Empty;

    internal AssetRef(Identity identity)
    {
        this.identity = identity;
    }

    /// <summary>
    /// Returns a human-readable handle representation.
    /// </summary>
    public override string ToString()
    {
        if (!isValid)
            return $"{typeof(TAsset).Name} (Invalid)";

        return $"{typeof(TAsset).Name} [{identity.persistentId}] (runtime: {identity.runtimeId?.ToString() ?? "null"})";
    }
}
