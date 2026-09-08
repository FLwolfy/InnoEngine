using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Inno.Core.Execution;

namespace Inno.Rendering.Runtime;

internal sealed class RenderPipelineGeneration : IDisposable
{
    private readonly LifetimeScope m_lifetime = new();
    private readonly Dictionary<string, RenderPipelineFeature> m_features = new(StringComparer.Ordinal);

    internal RenderPipelineGeneration(long typeCacheVersion, RenderPipeline pipeline)
    {
        this.typeCacheVersion = typeCacheVersion;
        this.pipeline = m_lifetime.Own(pipeline);
        features = new ReadOnlyDictionary<string, RenderPipelineFeature>(m_features);
    }

    internal long typeCacheVersion { get; }
    internal RenderPipeline pipeline { get; }
    internal IReadOnlyDictionary<string, RenderPipelineFeature> features { get; }

    internal void AddFeature(string id, RenderPipelineFeature feature)
    {
        if (feature is IDisposable resource)
            m_lifetime.Own(resource);
        m_features.Add(id, feature);
    }

    /// <summary>
    /// Retires features before their pipeline, preserving the remaining ownership while work is pending.
    /// </summary>
    public void Dispose() => m_lifetime.Dispose();
}
