
namespace Inno.Graphics;

/// <summary>
/// Describes a color attachment.
/// </summary>

public sealed class GraphicsColorAttachmentDescription
{
    public PixelFormat format { get; init; } = PixelFormat.B8G8R8A8Unorm;
}
