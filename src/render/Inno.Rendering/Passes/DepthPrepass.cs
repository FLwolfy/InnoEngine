
namespace Inno.Rendering;

/// <summary>
/// Renders depth-only geometry before the opaque pass.
/// </summary>
public sealed class DepthPrepass : RenderPass
{
    public DepthPrepass() : base("DepthPrepass", RenderPassEvent.DepthPrepass)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.DepthOnly);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.DepthOnly);
    }
}
