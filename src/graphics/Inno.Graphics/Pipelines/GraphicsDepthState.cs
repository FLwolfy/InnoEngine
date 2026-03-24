
namespace Inno.Graphics;

/// <summary>
/// Describes depth and stencil state.
/// </summary>

public sealed class GraphicsDepthState
{
    public bool depthTestEnabled { get; init; } = true;

    public bool depthWriteEnabled { get; init; } = true;

    public GraphicsCompareOp compareOp { get; init; } = GraphicsCompareOp.LessEqual;
}
