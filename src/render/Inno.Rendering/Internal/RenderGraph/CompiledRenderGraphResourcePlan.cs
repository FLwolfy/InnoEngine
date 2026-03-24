namespace Inno.Rendering;

internal sealed class CompiledRenderGraphResourcePlan
{
    public static CompiledRenderGraphResourcePlan EMPTY { get; } = new()
    {
        resources = Array.Empty<CompiledRenderGraphResource>()
    };

    public required IReadOnlyList<CompiledRenderGraphResource> resources { get; init; }
}
