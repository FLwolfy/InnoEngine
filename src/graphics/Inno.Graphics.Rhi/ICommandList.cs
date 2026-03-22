using System;
using Inno.Core.Mathematics;

namespace Inno.Platform.Graphics;

public interface ICommandList : IDisposable
{
    void Begin();
    void End();

    void SetFrameBuffer(IFrameBuffer frameBuffer);
    void SetViewPort(uint frameBufferIndex, Rect viewPortArea);
    void SetScissorRect(uint frameBufferIndex, Rect scissorRect);
    void SetVertexBuffer(IVertexBuffer vertexBuffer);
    void SetIndexBuffer(IIndexBuffer indexBuffer);
    void SetResourceSet(int setIndex, IResourceSet resourceSet);
    void SetPipelineState(IPipelineState pipelineState);
    
    void UpdateUniform<T>(IUniformBuffer uniformBuffer, ref T data) where T : unmanaged;
    
    void Draw(uint vertexCount, uint startVertex = 0);
    void DrawIndexed(uint indexCount, uint startIndex = 0, int baseVertex = 0);

    void ClearColor(Color color);
    void ClearDepth(float depth);
}