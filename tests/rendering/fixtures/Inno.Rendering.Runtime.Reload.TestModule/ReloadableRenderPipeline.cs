using Inno.Rendering;

namespace Inno.Rendering.Runtime.Reload.TestModule;

/// <summary>
/// Provides a collectible value used to verify that frame-data queues release retired plugins.
/// </summary>
public sealed class ReloadableFramePayload;

/// <summary>
/// Supplies a collectible Plugin-owned pipeline for rendering generation removal tests.
/// </summary>
[RenderPipelineExtension(extensionId)]
internal sealed class ReloadableRenderPipeline : RenderPipeline
{
    /// <summary>
    /// Identifies the pipeline independently of its collectible CLR generation.
    /// </summary>
    internal const string extensionId = "tests.runtime.reloadable-plugin";

    /// <summary>
    /// Adds observable render work to the frame graph.
    /// </summary>
    /// <param name="context">
    /// The active pipeline context that owns the frame graph mutation.
    /// </param>
    public override void Build(RenderPipelineContext context)
    {
        context.graph
            .AddRasterPass(
                "Reloadable Plugin Pass",
                new RenderPhaseId("tests.runtime.reloadable-plugin.visible"),
                0,
                static (_, _) => { })
            .HasSideEffect();
    }
}

[RenderFeatureExtension(extensionId)]
internal sealed class ReloadableRenderFeature : RenderPipelineFeature
{
    internal const string extensionId = "tests.runtime.reloadable-plugin-feature";

    /// <summary>
    /// Adds observable feature work to the active pipeline graph.
    /// </summary>
    /// <param name="context">
    /// The active feature context that owns the frame graph mutation.
    /// </param>
    public override void AddRenderPasses(RenderFeatureContext context)
    {
        context.graph
            .AddRasterPass(
                "Reloadable Plugin Feature Pass",
                new RenderPhaseId("tests.runtime.reloadable-plugin-feature.visible"),
                1,
                static (_, _) => { })
            .HasSideEffect();
    }
}
