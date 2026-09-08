using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;

namespace Inno.Scene;

/// <summary>
/// Carries immutable scene or prefab runtime content across the runtime-to-authoring asset boundary.
/// </summary>
public sealed class EngineAssetContent
{
    private readonly byte[] m_payload;
    private readonly AssetDependency[] m_dependencies;

    /// <summary>
    /// Creates a detached engine asset content snapshot.
    /// </summary>
    /// <param name="payload">
    /// The complete serialized runtime graph.
    /// </param>
    /// <param name="dependencies">
    /// The direct persistent asset dependencies of the graph.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="payload"/> is empty.
    /// </exception>
    public EngineAssetContent(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<AssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        if (payload.IsEmpty)
            throw new ArgumentException("Engine asset content cannot be empty.", nameof(payload));
        m_payload = payload.ToArray();
        m_dependencies = dependencies.ToArray();
    }

    /// <summary>
    /// Gets a detached copy of the serialized runtime graph.
    /// </summary>
    /// <returns>
    /// A payload copy owned by the caller.
    /// </returns>
    public byte[] GetPayload() => (byte[])m_payload.Clone();

    /// <summary>
    /// Gets a detached copy of direct persistent asset dependencies.
    /// </summary>
    /// <returns>
    /// A dependency array owned by the caller.
    /// </returns>
    public AssetDependency[] GetDependencies() => (AssetDependency[])m_dependencies.Clone();
}
