
namespace Inno.Graphics;

/// <summary>
/// Main contract for GPU resource creation and frame submission.
/// </summary>
public interface IGraphicsDevice : IDisposable
{
    GraphicsBackendKind backendKind { get; }

    GraphicsLimits limits { get; }

    IGraphicsSwapchain CreateSwapchain(GraphicsSwapchainDescription description);

    IGraphicsBuffer CreateBuffer(BufferDescription description);

    IGraphicsTexture CreateTexture(TextureDescription description);

    IGraphicsSampler CreateSampler(SamplerDescription description);

    IGraphicsShader CreateShader(ShaderDescription description);

    IGraphicsProgram CreateProgram(GraphicsProgramDescription description);

    IGraphicsInputLayout CreateInputLayout(GraphicsInputLayoutDescription description);

    IGraphicsRenderPipeline CreateRenderPipeline(GraphicsRenderPipelineDescription description);

    IGraphicsRenderTarget CreateRenderTarget(GraphicsRenderTargetDescription description);

    IGraphicsResourceSet CreateResourceSet(ResourceSetDescription description);

    IGraphicsCommandList CreateCommandList();

    IGraphicsContext CreateContext();

    void Submit(IGraphicsCommandList commandList);

    void BeginFrame();

    void EndFrame();

    void WaitIdle();
}
