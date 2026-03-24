
namespace Inno.Rendering;

/// <summary>
/// Represents a window backbuffer render target.
/// </summary>
public sealed class BackbufferTarget : RenderTarget
{
    public BackbufferTarget(RenderWindow window)
    {
        this.window = window;
    }

    public RenderWindow window { get; }

    public override int width => window.width;

    public override int height => window.height;

    public override Texture? colorTexture => null;

    public override Texture? depthTexture => null;
}
