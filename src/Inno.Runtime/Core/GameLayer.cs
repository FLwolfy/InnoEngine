using Inno.Core.ECS;
using Inno.Core.Layers;
using Inno.Graphics;
using Inno.Graphics.Pass;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;
using Inno.Runtime.RenderPasses;

namespace Inno.Runtime.Core;

public class GameLayer : Layer
{
    private readonly RenderTarget m_renderTarget;
    private readonly RenderPassStack m_renderPasses;

    public GameLayer() : base("GameLayer")
    {
        // Render Target
        var renderTexDesc = new TextureDescription
        {
            format = PixelFormat.R8_G8_B8_A8_UNorm,
            usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            dimension = TextureDimension.Texture2D
        };
        var depthTexDesc = new TextureDescription
        {
            format = PixelFormat.D32_Float_S8_UInt,
            usage = TextureUsage.DepthStencil,
            dimension = TextureDimension.Texture2D
        };
        var renderTargetDesc = new FrameBufferDescription
        {
            depthAttachmentDescription = depthTexDesc,
            colorAttachmentDescriptions = [renderTexDesc]
        };
        m_renderTarget = RenderGraphics.targetPool.Create("scene", renderTargetDesc);
        m_renderTarget = RenderGraphics.targetPool.GetMain(); // TODO: Remove this until texture blit is supported
        
        // Render Passes
        m_renderPasses = new RenderPassStack();
        m_renderPasses.PushPass(new ClearScreenPass());
        m_renderPasses.PushPass(new RenderOpaqueMeshPass()); 
        m_renderPasses.PushPass(new RenderOpaqueSpritePass());
        m_renderPasses.PushPass(new RenderAlphaSpritePass());
    }
    
    public override void OnAttach()
    {
        SceneManager.BeginRuntime();
    }

    public override void OnDetach()
    {
        m_renderTarget.Dispose();
    }

    public override void OnUpdate()
    {
        SceneManager.UpdateActiveScene();
    }

    public override void OnRender()
    {
        // Get Camera
        var camera = SceneManager.GetActiveScene()?.GetMainCamera();
        if (camera == null) { return; }
        
        // TODO: Use Renderer2D Blit
        m_renderTarget.GetRenderContext().BeginFrame(camera.viewMatrix * camera.projectionMatrix, camera.aspectRatio);
        m_renderPasses.OnRender(m_renderTarget.GetRenderContext());
        m_renderTarget.GetRenderContext().EndFrame();
    }

}