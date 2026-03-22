using System;
using Inno.Core.Math;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Targets;

public class RenderContext : IDisposable
{
    private readonly IGraphicsDevice m_graphicsDevice;
    private readonly bool m_hasDepth;
    private readonly bool m_isFromSwapchain;
    
    internal ICommandList commandList { get; }
    internal IFrameBuffer frameBuffer { get; }
    
    public Matrix viewProjection { get; private set; }

    internal RenderContext(IGraphicsDevice graphicsDevice, IFrameBuffer swapchainFrameBuffer)
    {
        commandList = graphicsDevice.CreateCommandList();
        frameBuffer = swapchainFrameBuffer;
        
        m_graphicsDevice = graphicsDevice;
        m_hasDepth = frameBuffer.GetDepthAttachment() != null;
        m_isFromSwapchain = true;
    }

    internal RenderContext(IGraphicsDevice graphicsDevice, FrameBufferDescription desc)
    {
        commandList = graphicsDevice.CreateCommandList();
        frameBuffer = graphicsDevice.CreateFrameBuffer(desc);
        
        m_graphicsDevice = graphicsDevice;
        m_hasDepth = frameBuffer.GetDepthAttachment() != null;
        m_isFromSwapchain = false;
    }

    public void BeginFrame(Matrix viewProjectionMatrix, float? aspectRatio)
    {
        viewProjection = viewProjectionMatrix;
        
        commandList.Begin();
        commandList.SetFrameBuffer(frameBuffer);
        commandList.ClearColor(Color.BLACK);

        if (aspectRatio.HasValue)
        {
            float targetWidth = frameBuffer.width;
            float targetHeight = frameBuffer.height;

            float screenAspect = targetWidth / targetHeight;
            float sourceAspect = aspectRatio.Value;

            Rect viewportRect;

            // Left Right
            if (screenAspect > sourceAspect)
            {
                float newWidth = targetHeight * sourceAspect;
                float xOffset = (targetWidth - newWidth) / 2f;
                viewportRect = new Rect((int)xOffset, 0, (int)newWidth, (int)targetHeight);
            }
        
            // Top Bottom
            else
            {
                float newHeight = targetWidth / sourceAspect;
                float yOffset = (targetHeight - newHeight) / 2f;
                viewportRect = new Rect(0, (int)yOffset, (int)targetWidth, (int)newHeight);
            }

            commandList.SetViewPort(0, viewportRect);
            commandList.SetScissorRect(0, viewportRect);
        }

        if (m_hasDepth)
        {
            commandList.ClearDepth(1.0f);
        }
    }
    
    public void EndFrame()
    {
        commandList.End();
        m_graphicsDevice.Submit(commandList);
    }

    public void Dispose()
    {
        commandList.Dispose();

        if (!m_isFromSwapchain)
        {
            frameBuffer.Dispose();
        }
    }
}