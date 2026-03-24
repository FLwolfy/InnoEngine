
namespace Inno.Rendering;

/// <summary>
/// Internal pipeline execution context.
/// </summary>
internal sealed class RenderPipelineContext
{
    public required RenderRequest request { get; init; }

    public required RenderFrame frame { get; init; }

    public required RenderResourceCache resourceCache { get; init; }

    public GraphicsRenderRuntime? graphics { get; init; }
}
