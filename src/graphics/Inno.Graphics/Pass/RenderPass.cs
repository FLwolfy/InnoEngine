using Inno.Engine.Graphics.Targets;

namespace Inno.Engine.Graphics.Pass;

/// <summary>
/// Defines the order of rendering passes.
/// </summary>
public enum RenderPassTag
{
    ClearScreen,
    Background,
    Geometry,
    Transparent,
    Lighting,
    PostProcessing,
    UI
}

/// <summary>
/// Represents a rendering stage in the pipeline.
/// </summary>
public abstract class RenderPass
{
    public abstract RenderPassTag orderTag { get; }
    
    /// <summary>
    /// This method is used for each concrete renderPass class to render details based on the given RenderContext.
    /// </summary>
    public abstract void OnRender(RenderContext ctx);
}