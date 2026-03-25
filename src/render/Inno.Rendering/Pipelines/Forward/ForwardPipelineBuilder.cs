
namespace Inno.Rendering;

/// <summary>
/// Represents mutable settings for creating a forward pipeline.
/// </summary>
public sealed class ForwardPipelineBuilder
{
    private readonly List<RenderFeature> m_features = [];
    private readonly List<IForwardPassProvider> m_passProviders = [];

    public ForwardPipelineBuilder()
    {
        foreach (var provider in ForwardPipeline.CreateDefaultPassProviders())
        {
            m_passProviders.Add(provider);
        }
    }

    public bool enableDepthPrepass { get; set; }

    public bool enableShadows { get; set; }

    public bool enableSkybox { get; set; } = true;

    public bool enableTransparentPass { get; set; } = true;

    public bool enablePostProcessing { get; set; } = true;

    public bool enableGizmos { get; set; }

    public bool enableObjectPicking { get; set; }

    public bool enableUiPass { get; set; } = true;

    public ForwardPipelineBuilder AddFeature(RenderFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        m_features.Add(feature);
        return this;
    }

    public ForwardPipelineBuilder AddPassProvider(IForwardPassProvider passProvider)
    {
        ArgumentNullException.ThrowIfNull(passProvider);
        m_passProviders.Add(passProvider);
        return this;
    }

    public ForwardPipeline Build()
    {
        var features = new PipelineFeatureSet
        {
            enableDepthPrepass = enableDepthPrepass,
            enableShadows = enableShadows,
            enableSkybox = enableSkybox,
            enableTransparentPass = enableTransparentPass,
            enablePostProcessing = enablePostProcessing,
            enableGizmos = enableGizmos,
            enableObjectPicking = enableObjectPicking,
            enableUiPass = enableUiPass
        };

        return ForwardPipeline.FromProviders(features, m_passProviders, m_features);
    }
}
