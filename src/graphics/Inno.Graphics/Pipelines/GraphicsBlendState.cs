
namespace Inno.Graphics;

/// <summary>
/// Describes blending state.
/// </summary>

public sealed class GraphicsBlendState
{
    public bool enabled { get; init; }

    public GraphicsBlendFactor srcColorFactor { get; init; } = GraphicsBlendFactor.One;

    public GraphicsBlendFactor dstColorFactor { get; init; } = GraphicsBlendFactor.Zero;

    public GraphicsBlendOp colorOp { get; init; } = GraphicsBlendOp.Add;

    public GraphicsBlendFactor srcAlphaFactor { get; init; } = GraphicsBlendFactor.One;

    public GraphicsBlendFactor dstAlphaFactor { get; init; } = GraphicsBlendFactor.Zero;

    public GraphicsBlendOp alphaOp { get; init; } = GraphicsBlendOp.Add;
}
