using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxSampler : DisposableGraphicsResource, IGraphicsSampler
{
    public BgfxSampler(SamplerDescription description)
    {
        this.description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public SamplerDescription description { get; }
}

