
namespace Inno.Graphics;

/// <summary>
/// Describes program composition from shader stages.
/// </summary>
public sealed class GraphicsProgramDescription
{
    public required IReadOnlyList<IGraphicsShader> shaders { get; init; }
}

/// <summary>
/// Describes a vertex element.
/// </summary>
public sealed class GraphicsVertexElement
{
    public required string semantic { get; init; }

    public int semanticIndex { get; init; }

    public VertexFormat format { get; init; }

    public int offset { get; init; }
}

/// <summary>
/// Describes input layout creation.
/// </summary>
public sealed class GraphicsInputLayoutDescription
{
    public required IReadOnlyList<GraphicsVertexElement> elements { get; init; }

    public int stride { get; init; }
}

/// <summary>
/// Describes render pass layout.
/// </summary>
public sealed class GraphicsRenderPassLayoutDescription
{
    public IReadOnlyList<GraphicsColorAttachmentDescription> colorAttachments { get; init; } = [];

    public GraphicsDepthAttachmentDescription? depthAttachment { get; init; }
}

/// <summary>
/// Describes a color attachment.
/// </summary>
public sealed class GraphicsColorAttachmentDescription
{
    public PixelFormat format { get; init; } = PixelFormat.B8G8R8A8Unorm;
}

/// <summary>
/// Describes a depth attachment.
/// </summary>
public sealed class GraphicsDepthAttachmentDescription
{
    public PixelFormat format { get; init; } = PixelFormat.D24UnormS8Uint;

    public bool readOnly { get; init; }
}

/// <summary>
/// Describes fixed-function raster state.
/// </summary>
public sealed class GraphicsRasterState
{
    public GraphicsCullMode cullMode { get; init; } = GraphicsCullMode.Back;

    public GraphicsFillMode fillMode { get; init; } = GraphicsFillMode.Solid;

    public bool frontFaceCounterClockwise { get; init; }
}

/// <summary>
/// Describes depth and stencil state.
/// </summary>
public sealed class GraphicsDepthState
{
    public bool depthTestEnabled { get; init; } = true;

    public bool depthWriteEnabled { get; init; } = true;

    public GraphicsCompareOp compareOp { get; init; } = GraphicsCompareOp.LessEqual;
}

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
