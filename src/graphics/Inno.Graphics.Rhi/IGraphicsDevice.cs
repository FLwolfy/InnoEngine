using System;

namespace Inno.Platform.Graphics;

public enum GraphicsBackend
{
    OpenGL,
    OpenGLES,
    Metal,
    Vulkan,
    Direct3D11
}

public interface IGraphicsDevice : IDisposable
{
    GraphicsBackend backend { get; }
    IFrameBuffer swapchainFrameBuffer { get; }
    
    IVertexBuffer CreateVertexBuffer(uint sizeInBytes);
    IIndexBuffer CreateIndexBuffer(uint sizeInBytes);
    IUniformBuffer CreateUniformBuffer(string name, Type type);
    
    IFrameBuffer CreateFrameBuffer(FrameBufferDescription desc);
    IResourceSet CreateResourceSet(ResourceSetBinding binding);
    (IShader, IShader) CreateVertexFragmentShader(ShaderDescription vertDesc, ShaderDescription fragDesc);
    IShader CreateComputeShader(ShaderDescription desc);
    
    ITexture CreateTexture(TextureDescription desc);
    ISampler CreateSampler(SamplerDescription desc);
    IPipelineState CreatePipelineState(PipelineStateDescription desc);
    
    ICommandList CreateCommandList();
    
    void Submit(ICommandList commandList);
}