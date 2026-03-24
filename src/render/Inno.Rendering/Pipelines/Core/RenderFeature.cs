namespace Inno.Rendering;

/// <summary>
/// Extensible renderer feature entry point.
/// </summary>
public abstract class RenderFeature
{
    public virtual bool enabled => true;

    public abstract void AddRenderPasses(RenderFeatureContext context, List<RenderPass> passes);
}
