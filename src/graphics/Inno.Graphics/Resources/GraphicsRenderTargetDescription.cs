
namespace Inno.Graphics;

/// <summary>
/// Describes render target attachments.
/// </summary>
public sealed class GraphicsRenderTargetDescription
{
    public IReadOnlyList<PixelFormat> colorFormats { get; init; } = [PixelFormat.B8G8R8A8Unorm];

    public PixelFormat? depthFormat { get; init; }

    public int width { get; init; }

    public int height { get; init; }

    public bool useBackbuffer { get; init; }
}
