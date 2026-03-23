using Inno.Graphics;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxGraphicsContext : IGraphicsContext
{
    public BgfxGraphicsContext(IGraphicsDevice device)
    {
        this.device = device;
    }

    public IGraphicsDevice device { get; }
}
