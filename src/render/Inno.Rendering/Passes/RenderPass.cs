namespace Inno.Rendering;

/// <summary>
/// Represents a render pipeline pass.
/// </summary>
public abstract class RenderPass
{
    protected RenderPass(string name, RenderPassEvent passEvent)
    {
        this.name = name;
        this.passEvent = passEvent;
    }

    public string name { get; }

    public RenderPassEvent passEvent { get; }

    public bool enabled { get; set; } = true;

    internal virtual void Setup(RenderGraphPassBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }

    internal abstract void Execute(RenderPassContext context);
}
