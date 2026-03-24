
namespace Inno.Rendering;

/// <summary>
/// Renders opaque objects.
/// </summary>
public sealed class OpaquePass : RenderPass
{
    public OpaquePass() : base("Opaque", RenderPassEvent.Opaque)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Opaque);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Opaque);
    }
}
