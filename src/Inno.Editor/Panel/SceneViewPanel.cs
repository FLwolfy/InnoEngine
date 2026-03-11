using System;
using ImGuizmoNET;
using Inno.Core.ECS;
using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.Gizmo;
using Inno.Editor.Utility;
using Inno.Graphics;
using Inno.Graphics.Pass;
using Inno.Graphics.Targets;
using Inno.Platform.Graphics;
using Inno.Runtime.RenderPasses;

using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public class SceneViewPanel : EditorPanel
{
    public override string title => "Scene";

    private static readonly Color AXIS_COLOR = new(0.5f, 0.5f, 0.5f, 0.5f);
    private static readonly float AXIS_THICKNESS = 1.0f;
    private static readonly int AXIS_INTERVAL = 100;
    private static readonly float AXIS_INTERVAL_SCALE_RATE = 0.5f;
    private static readonly Input.MouseButton MOUSE_BUTTON_PAN = Input.MouseButton.Left;

    private readonly EditorCamera2D m_editorCamera2D = new();
    private readonly GridGizmo m_gridGizmo = new();
    
    private RenderTarget m_renderTarget = null!;
    private RenderPassStack m_renderPasses = null!;
    private ITexture m_currentTexture = null!;
    
    private int m_width;
    private int m_height;
    private Rect m_viewRect;
    
    private readonly float[] m_gizmoView  = new float[16];
    private readonly float[] m_gizmoProj  = new float[16];
    private readonly float[] m_gizmoModel = new float[16];
    
    internal SceneViewPanel()
    {
        // Gizmos
        m_gridGizmo.showCoords = true;
        m_gridGizmo.color = AXIS_COLOR;
        m_gridGizmo.lineThickness = AXIS_THICKNESS;
        
        // Ensure scene rendering
        EnsureSceneRenderTarget();
        EnsureSceneRenderPasses();
    }
    
    internal override void OnGUI()
    {
        // Check if region changed
        CheckRegionChange();

        // Handle editorCamera action
        HandlePanZoom();

        // render and display scene on new render target
        RenderSceneToBuffer();

        // display on scene view
        DrawScene();
        
        // Draw axis gizmo
        DrawAxisGizmo();
        
        // Draw transform gizmo
        DrawTransformGizmo();
    }
    
    private void EnsureSceneRenderTarget()
    {
        if (RenderGraphics.targetPool.Get("scene") == null)
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
            
            m_renderTarget = RenderGraphics.targetPool.Create("scene", renderTargetDesc);
            m_currentTexture = m_renderTarget.GetColorAttachment(0)!;
        }
    }

    private void EnsureSceneRenderPasses()
    {
        m_renderPasses = new RenderPassStack();
        m_renderPasses.PushPass(new ClearScreenPass(Color.LIGHTGRAY));
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
            
            m_editorCamera2D.SetViewportSize(newWidth, newHeight);
            m_renderTarget.Resize(newWidth, newHeight);
        }
    }

    private void RenderSceneToBuffer()
    {
        if (RenderGraphics.targetPool.Get("scene") != null)
        {
            var flipYViewMatrix = m_editorCamera2D.viewMatrix;
            flipYViewMatrix.m42 *= -1;
            
            m_renderTarget.GetRenderContext().BeginFrame(m_editorCamera2D.viewMatrix * m_editorCamera2D.projectionMatrix, null);
            m_renderPasses.OnRender(m_renderTarget.GetRenderContext());
            m_renderTarget.GetRenderContext().EndFrame();
        }
    }
    
    private void HandlePanZoom()
    {
        var io = ImGuiNet.GetIO();

        float zoomDelta = io.MouseWheel;
        Vector2 localMousePos = io.MousePos - ImGuiNet.GetCursorScreenPos();

        bool isMouseInContent = localMousePos.y > 0 && ImGuiNet.IsWindowHovered();
        bool wantZoom = zoomDelta != 0.0f;
        bool wantPan = io.MouseDown[(int)MOUSE_BUTTON_PAN];

        Vector2 panDelta = Vector2.ZERO;

        // Only react when hovered (content)
        if (isMouseInContent && (wantPan || wantZoom))
        {
            // If you want: steal focus when interacting
            if (!ImGuiNet.IsWindowFocused())
                ImGuiNet.SetWindowFocus();

            if (wantPan)
                panDelta = io.MouseDelta;

            // Apply update only when hovered
            m_editorCamera2D.Update(panDelta, zoomDelta, localMousePos);
        }
    }


    private void DrawScene()
    {
        var targetTexture = RenderGraphics.targetPool.Get("scene")?.GetColorAttachment(0);
        if (targetTexture != null)
        {
            var newTextureHandle = ImGuiHost.GetOrBindTexture(targetTexture);
            if (m_currentTexture != targetTexture)
            {
                ImGuiHost.UnbindTexture(m_currentTexture);
                m_currentTexture = targetTexture;
            }
            
            ImGuiNet.Image(newTextureHandle, new Vector2(m_width, m_height));
            Vector2 min = ImGuiNet.GetItemRectMin();
            Vector2 max = ImGuiNet.GetItemRectMax();
            ImGuizmo.SetRect(min.x, min.y, max.x - min.x, max.y - min.y);
        }
    }
    
    private void DrawAxisGizmo()
    {
        Vector2 axisOriginWorld = Vector2.Transform(Vector2.ZERO, m_editorCamera2D.GetScreenToWorldMatrix());
        float spacing = Vector2.Transform(axisOriginWorld + new Vector2(AXIS_INTERVAL, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        int newAxisInterval = AXIS_INTERVAL;
        
        while (spacing < AXIS_INTERVAL * AXIS_INTERVAL_SCALE_RATE)
        {
            newAxisInterval = (int)(newAxisInterval / AXIS_INTERVAL_SCALE_RATE);
            spacing = Vector2.Transform(axisOriginWorld + new Vector2(newAxisInterval, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        }
        
        while (spacing > AXIS_INTERVAL / AXIS_INTERVAL_SCALE_RATE)
        {
            newAxisInterval = (int)(newAxisInterval * AXIS_INTERVAL_SCALE_RATE);
            spacing = Vector2.Transform(axisOriginWorld + new Vector2(newAxisInterval, 0), m_editorCamera2D.GetWorldToScreenMatrix()).x;
        }
        
        float offsetXWorld = (MathF.Floor(axisOriginWorld.x / newAxisInterval) + 1) * newAxisInterval;
        float offsetYWorld = (MathF.Floor(axisOriginWorld.y / newAxisInterval) + 1) * newAxisInterval;
        Vector2 offsetWorld = new Vector2(offsetXWorld, offsetYWorld);
        Vector2 offset = Vector2.Transform(offsetWorld, m_editorCamera2D.GetWorldToScreenMatrix());
        
        m_gridGizmo.startPos = ImGuiNet.GetWindowPos() + ImGuiNet.GetCursorStartPos();
        m_gridGizmo.size = new Vector2(m_width, m_height);
        m_gridGizmo.offset = offset;
        m_gridGizmo.spacing = spacing;
        m_gridGizmo.startCoords = offsetWorld;
        m_gridGizmo.coordsIncrement = new Vector2(newAxisInterval, newAxisInterval);
        
        m_gridGizmo.Draw();
    }

    private void DrawTransformGizmo()
    {
        ImGuizmo.SetOrthographic(true);

        if (EditorManager.selection.selectedObject is GameObject go)
        {
            // Check destroy or not
            if (go.transform == null) return;
            
            Matrix world = BuildWorldMatrix(go.transform);
            
            ToFloat16(m_editorCamera2D.viewMatrix, m_gizmoView);
            ToFloat16(m_editorCamera2D.projectionMatrix, m_gizmoProj);
            ToFloat16(world, m_gizmoModel);

            bool usingGizmo = ImGuizmo.Manipulate(
                ref m_gizmoView[0],
                ref m_gizmoProj[0],
                OPERATION.TRANSLATE,
                MODE.WORLD,
                ref m_gizmoModel[0]
            );
        }
    }

    public static void ToFloat16(Matrix m, float[] dst)
    {
        dst[0]  = m.m11; dst[1]  = m.m12; dst[2]  = m.m13; dst[3]  = m.m14;
        dst[4]  = m.m21; dst[5]  = m.m22; dst[6]  = m.m23; dst[7]  = m.m24;
        dst[8]  = m.m31; dst[9]  = m.m32; dst[10] = m.m33; dst[11] = m.m34;
        dst[12] = m.m41; dst[13] = m.m42; dst[14] = m.m43; dst[15] = m.m44;
    }

    public static Matrix FromFloat16(float[] m)
    {
        return new Matrix(
            m[0],  m[1],  m[2],  m[3],
            m[4],  m[5],  m[6],  m[7],
            m[8],  m[9],  m[10], m[11],
            m[12], m[13], m[14], m[15]
        );
    }
    
    private static Matrix BuildWorldMatrix(Transform t)
    {
        return
            Matrix.CreateScale(t.worldScale) *
            Matrix.CreateFromQuaternion(t.worldRotation) *
            Matrix.CreateTranslation(t.worldPosition);
    }

}