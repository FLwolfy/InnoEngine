
namespace Inno.Rendering;

/// <summary>
/// Renders object IDs for picking.
/// </summary>
public sealed class ObjectPickingPass : RenderPass
{
    public ObjectPickingPass() : base("ObjectPicking", RenderPassEvent.BeforePostProcess)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.ObjectPicking);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.ObjectPicking);
    }
}
