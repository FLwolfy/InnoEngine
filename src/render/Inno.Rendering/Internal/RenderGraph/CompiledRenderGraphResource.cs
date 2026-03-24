namespace Inno.Rendering;

internal sealed class CompiledRenderGraphResource
{
    public required string name { get; init; }

    public required int firstPassIndex { get; init; }

    public required int lastPassIndex { get; init; }

    public required bool isExternal { get; init; }

    public RenderTargetDescriptor? descriptor { get; init; }
}
