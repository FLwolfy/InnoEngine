using Inno.Core.Math;
using Veldrid;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridCommandList : ICommandList
{
    private Framebuffer m_currentFrameBuffer;
    
    internal CommandList inner { get; }

    public VeldridCommandList(CommandList commandList)
    {
        inner = commandList;
    }

    public void Begin()
    {
        inner.Begin();
    }

    public void End()
    {
        inner.End();
    }

    public void SetFrameBuffer(IFrameBuffer frameBuffer)
    {
        if (frameBuffer is VeldridFrameBuffer veldridFB)
        {
            inner.SetFramebuffer(veldridFB.inner);
            m_currentFrameBuffer = veldridFB.inner;
        }
    }
    
    public void SetViewPort(uint frameBufferIndex, Rect viewPortArea)
    {
        inner.SetViewport(frameBufferIndex, new Viewport(viewPortArea.x, viewPortArea.y, viewPortArea.width, viewPortArea.height, 0f, 1f));
    }

    public void SetScissorRect(uint frameBufferIndex, Rect scissorRect)
    {
        inner.SetScissorRect(frameBufferIndex, (uint)scissorRect.x, (uint)scissorRect.y, (uint)scissorRect.width, (uint)scissorRect.height);
    }

    public void SetVertexBuffer(IVertexBuffer vertexBuffer)
    {
        if (vertexBuffer is VeldridVertexBuffer veldridVB)
            inner.SetVertexBuffer(0, veldridVB.inner);
    }

    public void SetIndexBuffer(IIndexBuffer indexBuffer)
    {
        if (indexBuffer is VeldridIndexBuffer veldridIB)
            inner.SetIndexBuffer(veldridIB.inner, IndexFormat.UInt32);
    }

    public void SetResourceSet(int setIndex, IResourceSet resourceSet)
    {
        if (resourceSet is VeldridResourceSet veldridRS)
            inner.SetGraphicsResourceSet((uint)setIndex, veldridRS.inner);
    }

    public void SetPipelineState(IPipelineState pipelineState)
    {
        if (pipelineState is VeldridPipelineState veldridPS)
        {
            veldridPS.ValidateFrameBufferOutputDesc(m_currentFrameBuffer.OutputDescription);
            inner.SetPipeline(veldridPS.inner[m_currentFrameBuffer.OutputDescription]);
        }
    }

    public void UpdateUniform<T>(IUniformBuffer uniformBuffer, ref T data) where T : unmanaged
    {
        if (uniformBuffer is VeldridUniformBuffer veldridUB)
        {
            inner.UpdateBuffer(veldridUB.inner, 0, ref data);
        }
    }

    public void Draw(uint vertexCount, uint startVertex = 0)
    {
        inner.Draw(vertexCount, 1, startVertex, 0);
    }

    public void DrawIndexed(uint indexCount, uint startIndex = 0, int baseVertex = 0)
    {
        inner.DrawIndexed(indexCount, 1, startIndex, baseVertex, 0);
    }

    public void ClearColor(Color color)
    {
        inner.ClearColorTarget(0, new RgbaFloat(color.r, color.g, color.b, color.a));
    }

    public void ClearDepth(float depth)
    {
        inner.ClearDepthStencil(depth);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
