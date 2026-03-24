
namespace Inno.Rendering;

/// <summary>
/// Represents serialized pipeline asset metadata.
/// </summary>
public sealed class RenderPipelineAsset
{
    public string name { get; set; } = "ForwardPipeline";

    public PipelineFeatureSet features { get; set; } = new();

    public List<RenderFeature> customFeatures { get; } = [];

    public RenderPipeline CreatePipeline() => ForwardPipeline.FromFeatureSet(features, customFeatures);
}
