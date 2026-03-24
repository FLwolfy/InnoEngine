
namespace Inno.Rendering;

/// <summary>
/// Renders debug gizmos.
/// </summary>
public sealed class GizmoPass : RenderPass
{
    public GizmoPass() : base("Gizmo", RenderPassEvent.BeforeUi)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Gizmo);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Gizmo);
    }
}
