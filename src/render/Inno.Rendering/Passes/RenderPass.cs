using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Represents a render pipeline pass.
/// </summary>
public abstract class RenderPass
{
    protected RenderPass(string name)
    {
        this.name = name;
    }

    public string name { get; }

    public bool enabled { get; set; } = true;

    internal abstract void Execute(RenderPassContext context);
}

public sealed class OpaquePass : RenderPass
{
    public OpaquePass() : base("Opaque")
    {
    }

    internal override void Execute(RenderPassContext context) => context.renderList.Build(RenderItemFilter.Opaque);
}

public sealed class TransparentPass : RenderPass
{
    public TransparentPass() : base("Transparent")
    {
    }

    internal override void Execute(RenderPassContext context) => context.renderList.Build(RenderItemFilter.Transparent);
}

public sealed class ShadowPass : RenderPass
{
    public ShadowPass() : base("Shadow")
    {
    }

    internal override void Execute(RenderPassContext context) => context.renderList.Build(RenderItemFilter.ShadowCasters);
}

public sealed class SkyboxPass : RenderPass
{
    public SkyboxPass() : base("Skybox")
    {
    }

    internal override void Execute(RenderPassContext context)
    {
    }
}

public sealed class GizmoPass : RenderPass
{
    public GizmoPass() : base("Gizmo")
    {
    }

    internal override void Execute(RenderPassContext context)
    {
    }
}

public sealed class UiPass : RenderPass
{
    public UiPass() : base("UI")
    {
    }

    internal override void Execute(RenderPassContext context)
    {
    }
}

public sealed class DepthPrepass : RenderPass
{
    public DepthPrepass() : base("DepthPrepass")
    {
    }

    internal override void Execute(RenderPassContext context) => context.renderList.Build(RenderItemFilter.DepthOnly);
}

public sealed class PostProcessPass : RenderPass
{
    public PostProcessPass() : base("PostProcess")
    {
    }

    internal override void Execute(RenderPassContext context)
    {
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
