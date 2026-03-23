using Inno.Graphics;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxCommandList : DisposableGraphicsResource, IGraphicsCommandList
{
    public void Begin()
    {
    }

    public void End()
    {
    }

    public void BeginRenderPass(IGraphicsRenderTarget renderTarget, ClearValue clearValue)
    {
        throw new NotImplementedException("bgfx render pass translation is not implemented yet.");
    }

    public void EndRenderPass()
    {
        throw new NotImplementedException("bgfx render pass translation is not implemented yet.");
    }

    public void SetViewport(GraphicsViewport viewport)
    {
    }

    public void SetScissorRect(GraphicsScissorRect rect)
    {
    }

    public void SetPipeline(IGraphicsRenderPipeline pipeline)
    {
        throw new NotImplementedException("bgfx pipeline binding is not implemented yet.");
    }

    public void SetVertexBuffer(IGraphicsBuffer vertexBuffer, int slot = 0)
    {
        throw new NotImplementedException("bgfx vertex buffer binding is not implemented yet.");
    }

    public void SetIndexBuffer(IGraphicsBuffer indexBuffer)
    {
        throw new NotImplementedException("bgfx index buffer binding is not implemented yet.");
    }

    public void SetResourceSet(int slot, IGraphicsResourceSet resourceSet)
    {
        throw new NotImplementedException("bgfx resource set binding is not implemented yet.");
    }

    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        throw new NotImplementedException("bgfx draw translation is not implemented yet.");
    }

    public void DrawIndexed(DrawIndexedArguments args)
    {
        throw new NotImplementedException("bgfx indexed draw translation is not implemented yet.");
    }
}
