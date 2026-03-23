using Inno.Graphics;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxGraphicsDevice : IGraphicsDevice
{
    public GraphicsBackendKind backendKind => GraphicsBackendKind.Bgfx;

    public GraphicsLimits limits { get; } = new();

    public IGraphicsSwapchain CreateSwapchain(GraphicsSwapchainDescription description) => new BgfxSwapchain(description);

    public IGraphicsBuffer CreateBuffer(BufferDescription description) => new BgfxBuffer(description);

    public IGraphicsTexture CreateTexture(TextureDescription description) => new BgfxTexture(description);

    public IGraphicsSampler CreateSampler(SamplerDescription description) => new BgfxSampler();

    public IGraphicsShader CreateShader(ShaderDescription description) => new BgfxShader();

    public IGraphicsProgram CreateProgram(GraphicsProgramDescription description) => new BgfxProgram();

    public IGraphicsInputLayout CreateInputLayout(GraphicsInputLayoutDescription description) => new BgfxInputLayout();

    public IGraphicsRenderPipeline CreateRenderPipeline(GraphicsRenderPipelineDescription description) => new BgfxRenderPipeline();

    public IGraphicsRenderTarget CreateRenderTarget(GraphicsRenderTargetDescription description) => new BgfxRenderTarget(description);

    public IGraphicsResourceSet CreateResourceSet(ResourceSetDescription description) => new BgfxResourceSet();

    public IGraphicsCommandList CreateCommandList() => new BgfxCommandList();

    public IGraphicsContext CreateContext() => new BgfxGraphicsContext(this);

    public void Submit(IGraphicsCommandList commandList)
    {
        ArgumentNullException.ThrowIfNull(commandList);
        throw new NotImplementedException("bgfx submission is not implemented yet.");
    }

    public void BeginFrame()
    {
    }

    public void EndFrame()
    {
    }

    public void WaitIdle()
    {
    }

    public void Dispose()
    {
    }
}
