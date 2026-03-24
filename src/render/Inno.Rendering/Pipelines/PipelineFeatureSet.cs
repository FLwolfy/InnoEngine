
namespace Inno.Rendering;

/// <summary>
/// Represents feature toggles for the built-in forward pipeline.
/// </summary>
public sealed class PipelineFeatureSet
{
    public bool enableDepthPrepass { get; set; }

    public bool enableShadows { get; set; }

    public bool enableSkybox { get; set; } = true;

    public bool enableTransparentPass { get; set; } = true;

    public bool enablePostProcessing { get; set; } = true;

    public bool enableGizmos { get; set; }

    public bool enableObjectPicking { get; set; }

    public bool enableUiPass { get; set; } = true;
}
