
namespace Inno.Rendering;

/// <summary>
/// Renders skybox geometry.
/// </summary>
public sealed class SkyboxPass : RenderPass
{
    public SkyboxPass() : base("Skybox", RenderPassEvent.Skybox)
    {
    }

    internal override void Setup(RenderGraphPassBuilder builder)
    {
        builder.ReadWrite(RenderGraphResourceNames.Backbuffer);
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Skybox);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Skybox);
    }
}
