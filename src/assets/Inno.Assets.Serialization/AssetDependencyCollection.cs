using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Reflection;

namespace Inno.Assets.Serialization;

/// <summary>
/// Collects direct asset dependencies encountered by a serialization operation.
/// </summary>
/// <remarks>
/// Add this instance to a <see cref="Inno.Core.Serialization.SerializationContext"/> when
/// serializing an asset source that must publish dependency metadata.
/// </remarks>
public sealed class AssetDependencyCollection
{
    private readonly Dictionary<Guid, AssetDependency> m_dependencies = [];

    /// <summary>
    /// Gets the collected dependencies in deterministic persistent-identity order.
    /// </summary>
    public IReadOnlyList<AssetDependency> dependencies
        => m_dependencies.Values.OrderBy(static dependency => dependency.persistentId).ToArray();

    internal void Add(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Guid persistentId = asset.identity.persistentId;
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException($"Asset '{asset.GetType().FullName}' has no persistent identity.");
        if (!TypeCacheManager.TryGetStableTypeId(asset.GetType(), out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"Asset type '{asset.GetType().FullName}' requires a StableTypeId before it can be referenced persistently.");
        }

        m_dependencies[persistentId] = new AssetDependency(persistentId, stableTypeId, asset.sourcePath);
    }
}
