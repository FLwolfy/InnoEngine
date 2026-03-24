namespace Inno.Rendering;

/// <summary>
/// Provides contextual data for render feature pass injection.
/// </summary>
public sealed class RenderFeatureContext
{
    public required PipelineFeatureSet features { get; init; }
}

/// <summary>
/// Extensible renderer feature entry point.
/// </summary>
public abstract class RenderFeature
{
    public virtual bool enabled => true;

    public abstract void AddRenderPasses(RenderFeatureContext context, List<RenderPass> passes);
}
