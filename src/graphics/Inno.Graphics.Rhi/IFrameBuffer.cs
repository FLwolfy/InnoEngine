using System;

namespace Inno.Platform.Graphics;

public struct FrameBufferDescription()
{
    public int width = 1;
    public int height = 1;
    public TextureDescription? depthAttachmentDescription;
    public TextureDescription[] colorAttachmentDescriptions = null!;
}

public interface IFrameBuffer : IDisposable
{
    int width { get; }
    int height { get; }
    
    void Resize(int width, int height);

    ITexture? GetColorAttachment(int index);
    ITexture? GetDepthAttachment();
}
