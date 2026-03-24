using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxInputLayout : DisposableGraphicsResource, IGraphicsInputLayout
{
    public BgfxInputLayout(GraphicsInputLayoutDescription description)
    {
        this.description = description;
        nativeLayout = BgfxVertexLayoutConverter.Build(description);
    }

    public GraphicsInputLayoutDescription description { get; }

    internal bgfx.VertexLayout nativeLayout { get; }
}

