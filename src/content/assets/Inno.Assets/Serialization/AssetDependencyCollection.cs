using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets;
using Inno.Extensibility.Types;

namespace Inno.Assets;

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
    /// Creates a dependency collector that preserves last-known source paths in serialized references.
    /// </summary>
    public AssetDependencyCollection()
        : this(includeLastKnownPaths: true)
    {
    }

    /// <summary>
    /// Creates a dependency collector with an explicit location-hint policy.
    /// </summary>
    /// <param name="includeLastKnownPaths">
    /// Whether serialized asset references and collected dependency descriptors retain source path hints.
    /// Disable this only for semantic content comparison; persistent identity and stable type identity remain encoded.
    /// </param>
    public AssetDependencyCollection(bool includeLastKnownPaths)
    {
        this.includeLastKnownPaths = includeLastKnownPaths;
    }

    /// <summary>
    /// Gets whether serialized asset references and collected dependencies retain source path hints.
    /// </summary>
    public bool includeLastKnownPaths { get; }

    /// <summary>
    /// Gets the collected dependencies in deterministic persistent-identity order.
    /// </summary>
    public IReadOnlyList<AssetDependency> dependencies
        => m_dependencies.Values.OrderBy(static dependency => dependency.persistentId).ToArray();

    internal void Add(AssetObject asset, TypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(types);
        Guid persistentId = asset.identity.persistentId;
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException($"Asset '{asset.GetType().FullName}' has no persistent identity.");
        if (!types.TryGetTypeRef(asset.GetType(), out TypeRef typeRef))
        {
            throw new InvalidOperationException(
                $"Asset type '{asset.GetType().FullName}' requires a StableTypeId before it can be referenced persistently.");
        }

        m_dependencies[persistentId] = new AssetDependency(
            persistentId,
            typeRef,
            includeLastKnownPaths ? asset.assetPath.ToString() : string.Empty);
    }
}
