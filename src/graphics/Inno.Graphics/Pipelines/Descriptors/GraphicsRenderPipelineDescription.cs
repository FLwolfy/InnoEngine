
namespace Inno.Graphics;

/// <summary>
/// Describes immutable render pipeline creation.
/// </summary>

public sealed class GraphicsRenderPipelineDescription
{
    public required IGraphicsProgram program { get; init; }

    public required IGraphicsInputLayout inputLayout { get; init; }

    public GraphicsRenderPassLayoutDescription renderPassLayout { get; init; } = new();

    public GraphicsRasterState rasterState { get; init; } = new();

    public GraphicsDepthState depthState { get; init; } = new();

    public GraphicsBlendState blendState { get; init; } = new();
}
