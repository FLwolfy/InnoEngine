
namespace Inno.Rendering;

/// <summary>
/// Renders shadow caster geometry to shadow maps.
/// </summary>
public sealed class ShadowPass : RenderPass
{
    public ShadowPass() : base("Shadow", RenderPassEvent.Shadows)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.ShadowCasters);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.ShadowCasters);
    }
}
