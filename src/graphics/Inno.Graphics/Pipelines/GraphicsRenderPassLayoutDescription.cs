
namespace Inno.Graphics;

/// <summary>
/// Describes render pass layout.
/// </summary>

public sealed class GraphicsRenderPassLayoutDescription
{
    public IReadOnlyList<GraphicsColorAttachmentDescription> colorAttachments { get; init; } = [];

    public GraphicsDepthAttachmentDescription? depthAttachment { get; init; }
}
