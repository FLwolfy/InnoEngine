using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Assets;

/// <summary>
/// Counts explicit leases over immutable artifact keys independently of asset object residency.
/// </summary>
public sealed class ArtifactRetention : AssetResidencyProvider
{
    private readonly object m_sync = new();
    private readonly Dictionary<AssetArtifactKey, int> m_counts = [];

    /// <summary>
    /// Retains a verified output before its owner permits cache collection.
    /// </summary>
    /// <param name="artifact">
    /// Verified immutable metadata; the provider must serialize acquisition with collection.
    /// </param>
    /// <returns>
    /// A lease that releases exactly one reference, including when disposed from another thread.
    /// </returns>
    public ArtifactLease Retain(AssetArtifactInfo artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        lock (m_sync)
        {
            m_counts.TryGetValue(artifact.key, out int count);
            m_counts[artifact.key] = checked(count + 1);
        }
        return CreateArtifactLease(artifact, () => Release(artifact.key));
    }

    /// <summary>
    /// Captures the keys a collector must preserve regardless of catalog reachability.
    /// </summary>
    /// <returns>
    /// An immutable snapshot; the provider serializes collection with new acquisitions.
    /// </returns>
    public IReadOnlyList<AssetArtifactKey> GetRetainedKeys()
    {
        lock (m_sync)
            return Array.AsReadOnly(m_counts.Keys.ToArray());
    }

    private void Release(AssetArtifactKey key)
    {
        lock (m_sync)
        {
            int remaining = m_counts[key] - 1;
            if (remaining == 0)
                m_counts.Remove(key);
            else
                m_counts[key] = remaining;
        }
    }
}
