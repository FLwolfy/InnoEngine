
namespace Inno.Graphics;

/// <summary>
/// Describes a depth attachment.
/// </summary>

public sealed class GraphicsDepthAttachmentDescription
{
    public PixelFormat format { get; init; } = PixelFormat.D24UnormS8Uint;

    public bool readOnly { get; init; }
}
