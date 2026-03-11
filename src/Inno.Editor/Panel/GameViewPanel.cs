using System;

using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;
using Inno.Graphics;
using Inno.Graphics.Pass;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;
using Inno.Runtime.RenderPasses;

using ImGuiNET;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public class GameViewPanel : EditorPanel
{
    public override string title => "Game";
    
    private RenderTarget m_renderTarget = null!;
    private RenderPassStack m_renderPasses = null!;
    private ITexture m_currentTexture = null!;
    
    private int m_width;
    private int m_height;
    
    internal GameViewPanel()
    {
        // Ensure scene rendering
        EnsureSceneRenderTarget();
        EnsureSceneRenderPasses();
    }
    
    internal override void OnGUI()
    {
        // Check if region changed
        CheckRegionChange();

        // render and display scene on new render target
        RenderSceneToBuffer();

        // display on scene view
        DrawScene();
    }
    
    private void EnsureSceneRenderTarget()
    {
        if (RenderGraphics.targetPool.Get("game") == null)
        {
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
            
            m_renderTarget = RenderGraphics.targetPool.Create("game", renderTargetDesc);
            m_currentTexture = m_renderTarget.GetColorAttachment(0)!;
        }
    }

    private void EnsureSceneRenderPasses()
    {
        m_renderPasses = new RenderPassStack();
        m_renderPasses.PushPass(new ClearScreenPass());
        m_renderPasses.PushPass(new RenderOpaqueMeshPass()); 
        m_renderPasses.PushPass(new RenderOpaqueSpritePass());
        m_renderPasses.PushPass(new RenderAlphaSpritePass());
    }

    private void CheckRegionChange()
    {
        // Get Available region
        Vector2 available = ImGuiNet.GetContentRegionAvail();
        int newWidth = (int)Math.Max(available.x, 1);
        int newHeight = (int)Math.Max(available.y, 1);
        
        // if region change, resize
        if (newWidth != m_width || newHeight != m_height)
        {
            m_width = newWidth;
            m_height = newHeight;
            
            m_renderTarget.Resize(newWidth, newHeight);
        }
    }

    private void RenderSceneToBuffer()
    {
        if (RenderGraphics.targetPool.Get("game") != null)
        {
            var camera = SceneManager.GetActiveScene()?.GetMainCamera();
            if (camera == null) { return; }
            
            m_renderTarget.GetRenderContext().BeginFrame(camera.viewMatrix * camera.projectionMatrix, camera.aspectRatio);
            m_renderPasses.OnRender(m_renderTarget.GetRenderContext());
            m_renderTarget.GetRenderContext().EndFrame();
        }
    }

    private void DrawScene()
    {
        var targetTexture = RenderGraphics.targetPool.Get("game")?.GetColorAttachment(0);
        if (targetTexture != null)
        {
            var newTextureHandle = ImGuiHost.GetOrBindTexture(targetTexture);
            if (m_currentTexture != targetTexture)
            {
                ImGuiHost.UnbindTexture(m_currentTexture);
                m_currentTexture = targetTexture;
            }

            if (SceneManager.GetActiveScene()?.GetMainCamera() == null)
            {
                EditorGUILayout.BeginAlignment(EditorGUILayout.LayoutAlign.Center);
                EditorGUILayout.Label("No Main Camera Set!");
                EditorGUILayout.EndAlignment();
                return;
            }
            
            ImGuiNet.Image(newTextureHandle, new Vector2(m_width, m_height));
        }
    }
}