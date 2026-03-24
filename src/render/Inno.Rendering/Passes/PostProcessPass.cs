
namespace Inno.Rendering;

/// <summary>
/// Renders post-process effects.
/// </summary>
public sealed class PostProcessPass : RenderPass
{
    public PostProcessPass() : base("PostProcess", RenderPassEvent.PostProcess)
    {
    }

    internal override void Setup(RenderGraphPassBuilder builder)
    {
        builder.ReadWrite(RenderGraphResourceNames.Backbuffer);
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.PostProcess);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.PostProcess);
    }
}
