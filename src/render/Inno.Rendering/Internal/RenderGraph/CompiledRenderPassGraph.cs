namespace Inno.Rendering;

internal sealed class CompiledRenderPassGraph
{
    public required IReadOnlyList<RenderPass> orderedPasses { get; init; }

    public required IReadOnlyList<RenderGraphPassDeclaration> passDeclarations { get; init; }

    public required CompiledRenderGraphResourcePlan resourcePlan { get; init; }

    public required IReadOnlyDictionary<RenderPass, RenderGraphPassDeclaration> declarationByPass { get; init; }

    public bool TryGetDeclaration(RenderPass pass, out RenderGraphPassDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return declarationByPass.TryGetValue(pass, out declaration!);
    }
}
