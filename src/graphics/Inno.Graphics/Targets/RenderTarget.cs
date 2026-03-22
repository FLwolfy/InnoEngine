using System;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Targets;

public class RenderTarget : IDisposable
{
    private readonly RenderContext m_context;

    public int width => m_context.frameBuffer.width;
    public int height => m_context.frameBuffer.height;

    internal RenderTarget(RenderContext context)
    {
        m_context = context;
    }

    internal RenderTarget(IGraphicsDevice graphicsDevice, FrameBufferDescription desc)
    {
        m_context = new RenderContext(graphicsDevice, desc);
    }
    
    public void Resize(int w, int h) => m_context.frameBuffer.Resize(w, h);
    public RenderContext GetRenderContext() => m_context;
    public ITexture? GetDepthAttachment() => m_context.frameBuffer.GetDepthAttachment();
    public ITexture? GetColorAttachment(int index) => m_context.frameBuffer.GetColorAttachment(index);

    public void Dispose()
    {
        m_context.Dispose();
    }
}