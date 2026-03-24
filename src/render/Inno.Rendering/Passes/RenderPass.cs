
namespace Inno.Rendering;

/// <summary>
/// Defines pass scheduling points in the frame.
/// </summary>
public enum RenderPassEvent
{
    BeforeDepthPrepass = 100,
    DepthPrepass = 200,
    BeforeShadows = 300,
    Shadows = 400,
    BeforeOpaque = 500,
    Opaque = 600,
    Skybox = 700,
    BeforeTransparent = 800,
    Transparent = 900,
    BeforePostProcess = 1000,
    PostProcess = 1100,
    BeforeUi = 1200,
    Ui = 1300,
    AfterFrame = 1400
}

/// <summary>
/// Represents a render pipeline pass.
/// </summary>
public abstract class RenderPass
{
    protected RenderPass(string name, RenderPassEvent passEvent)
    {
        this.name = name;
        this.passEvent = passEvent;
    }

    public string name { get; }

    public RenderPassEvent passEvent { get; }

    public bool enabled { get; set; } = true;

    internal abstract void Execute(RenderPassContext context);
}

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

public sealed class SkyboxPass : RenderPass
{
    public SkyboxPass() : base("Skybox", RenderPassEvent.Skybox)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Skybox);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Skybox);
    }
}

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

public sealed class UiPass : RenderPass
{
    public UiPass() : base("UI", RenderPassEvent.Ui)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.Ui);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.Ui);
    }
}

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

public sealed class PostProcessPass : RenderPass
{
    public PostProcessPass() : base("PostProcess", RenderPassEvent.PostProcess)
    {
    }

    internal override void Execute(RenderPassContext context)
    {
        context.renderList.Build(RenderItemFilter.PostProcess);
        context.pipelineContext.graphics?.ExecutePass(context.pipelineContext, context.renderList, RenderItemFilter.PostProcess);
    }
}

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

/// <summary>
/// Internal pass execution context.
/// </summary>
internal sealed class RenderPassContext
{
    public required RenderPipelineContext pipelineContext { get; init; }

    public required RenderList renderList { get; init; }
}
