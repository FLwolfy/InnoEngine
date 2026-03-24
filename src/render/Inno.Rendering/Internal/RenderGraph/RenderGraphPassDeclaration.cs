namespace Inno.Rendering;

internal sealed class RenderGraphPassDeclaration
{
    public RenderGraphPassDeclaration(IReadOnlyList<RenderGraphResourceUsage> resources)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    public IReadOnlyList<RenderGraphResourceUsage> resources { get; }
}
