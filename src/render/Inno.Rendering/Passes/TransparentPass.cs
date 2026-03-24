
namespace Inno.Rendering;

/// <summary>
/// Renders transparent objects.
/// </summary>
public sealed class TransparentPass : RenderPass
{
    public TransparentPass() : base("Transparent", RenderPassEvent.Transparent)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Transparent);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Transparent);
    }
}
