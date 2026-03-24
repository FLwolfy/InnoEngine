
namespace Inno.Graphics;

/// <summary>
/// Records graphics commands for submission.
/// </summary>
public interface IGraphicsCommandList : IGraphicsResource
{
    void Begin();

    void End();

    void BeginRenderPass(IGraphicsRenderTarget renderTarget, ClearValue clearValue);

    void EndRenderPass();

    void SetViewport(GraphicsViewport viewport);

    void SetScissorRect(GraphicsScissorRect rect);

    void SetPipeline(IGraphicsRenderPipeline pipeline);

    void SetVertexBuffer(IGraphicsBuffer vertexBuffer, int slot = 0);

    void SetIndexBuffer(IGraphicsBuffer indexBuffer);

    void SetResourceSet(int slot, IGraphicsResourceSet resourceSet);

    void SetViewProjection(ReadOnlySpan<float> view, ReadOnlySpan<float> projection);

    void SetModelTransform(ReadOnlySpan<float> model);

    void SetGlobalVector4(string name, ReadOnlySpan<float> value);

    void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0);

    void DrawIndexed(DrawIndexedArguments args);
}
