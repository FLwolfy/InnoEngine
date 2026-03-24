namespace Inno.Rendering;

/// <summary>
/// Represents an extensible render pipeline.
/// </summary>
public abstract class RenderPipeline
{
    protected RenderPipeline(string name)
    {
        this.name = name;
    }

    public string name { get; }

    internal abstract void Render(RenderPipelineContext context);
}
