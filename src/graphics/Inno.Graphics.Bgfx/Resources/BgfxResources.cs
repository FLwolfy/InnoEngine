using Inno.Graphics;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxBuffer : DisposableGraphicsResource, IGraphicsBuffer
{
    public BgfxBuffer(BufferDescription description)
    {
        sizeInBytes = description.sizeInBytes;
        usage = description.usage;
    }

    public int sizeInBytes { get; }

    public GraphicsBufferUsage usage { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int destinationOffsetInBytes = 0) where T : unmanaged
    {
        throw new NotImplementedException("bgfx upload path is not implemented yet.");
    }
}

public sealed class BgfxTexture : DisposableGraphicsResource, IGraphicsTexture
{
    public BgfxTexture(TextureDescription description)
    {
        width = description.width;
        height = description.height;
        format = description.format;
    }

    public int width { get; }

    public int height { get; }

    public PixelFormat format { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int mipLevel = 0) where T : unmanaged
    {
        throw new NotImplementedException("bgfx texture upload is not implemented yet.");
    }
}

public sealed class BgfxSampler : DisposableGraphicsResource, IGraphicsSampler
{
}

public sealed class BgfxRenderTarget : DisposableGraphicsResource, IGraphicsRenderTarget
{
    public BgfxRenderTarget(GraphicsRenderTargetDescription description)
    {
        width = description.width;
        height = description.height;
    }

    public int width { get; }

    public int height { get; }
}

public sealed class BgfxResourceSet : DisposableGraphicsResource, IGraphicsResourceSet
{
}
