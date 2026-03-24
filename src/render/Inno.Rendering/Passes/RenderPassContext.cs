
namespace Inno.Rendering;

/// <summary>
/// Internal pass execution context.
/// </summary>
internal sealed class RenderPassContext
{
    public required RenderPipelineContext pipelineContext { get; init; }

    public required RenderList renderList { get; init; }
}
