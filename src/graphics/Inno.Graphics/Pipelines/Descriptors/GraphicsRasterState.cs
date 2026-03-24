
namespace Inno.Graphics;

/// <summary>
/// Describes fixed-function raster state.
/// </summary>

public sealed class GraphicsRasterState
{
    public GraphicsCullMode cullMode { get; init; } = GraphicsCullMode.Back;

    public GraphicsFillMode fillMode { get; init; } = GraphicsFillMode.Solid;

    public bool frontFaceCounterClockwise { get; init; }
}
