using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Math;
using Inno.Core.Utility;
using Inno.Editor.GUI;
using Inno.Editor.Panel;

using Inno.ImGui;
using Inno.Platform;

using ImGuizmoNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Core;

public class EditorLayer(PlatformRuntime platform) : Layer("EditorLayer")
{
    private static readonly float MIN_ZOOM_RATE = 0.2f;
    private static readonly float MAX_ZOOM_RATE = 4.0f;
    private static readonly float ZOOM_RATE_STEP = 0.1f;
    private static readonly float DEFAULT_ZOOM_RATE = 1.0f;

    private float m_currentZoomRate;

    public override void OnAttach()
    {
        // ImGui Initialization
        ImGuiHost.Initialize
        (
            platform.windowSystem, 
            platform.displaySystem,
            platform.graphicsDevice,
            ImGuiBackend.ImGui_DotNET, 
            ImGuiColorSpaceHandling.Legacy
        );
        
        // Zoom
        m_currentZoomRate = ImGuiHost.GetStorageData("Editor.ZoomRate", DEFAULT_ZOOM_RATE);
        ImGuiHost.Zoom(m_currentZoomRate);
        
        // MenuBar Setup
        // TODO: Setup MenuBar
        
        // Panel Registration
        EditorManager.RegisterPanel(new SceneViewPanel());
        EditorManager.RegisterPanel(new GameViewPanel());
        EditorManager.RegisterPanel(new FileBrowserPanel());
        EditorManager.RegisterPanel(new LogPanel());
        EditorManager.RegisterPanel(new HierarchyPanel());
        EditorManager.RegisterPanel(new InspectorPanel());
        
        // IO Loaded
        EditorSceneAssetIO.Initialize();
    }

    public override void OnEvent(Event e)
    {
        var keyEvent = e as KeyPressedEvent;
        if (keyEvent == null) return;
        
        HandleZoom(keyEvent);
        HandleSceneSave(keyEvent);
    }

    private void HandleZoom(KeyPressedEvent keyEvent)
    {
        if (keyEvent.key == Input.KeyCode.Plus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
        {
            m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate + ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
            ImGuiHost.SetStorageData("Editor.ZoomRate", m_currentZoomRate);
            ImGuiHost.Zoom(m_currentZoomRate);
        }
        
        if (keyEvent.key == Input.KeyCode.Minus && keyEvent.modifiers == Input.KeyModifier.Control && !keyEvent.repeat)
        {
            m_currentZoomRate = MathHelper.Clamp(m_currentZoomRate - ZOOM_RATE_STEP, MIN_ZOOM_RATE, MAX_ZOOM_RATE);
            ImGuiHost.SetStorageData("Editor.ZoomRate", m_currentZoomRate);
            ImGuiHost.Zoom(m_currentZoomRate);
        }
    }
    
    private void HandleSceneSave(KeyPressedEvent keyEvent)
    {
        if (keyEvent.repeat) return;
        if (keyEvent.key != Input.KeyCode.S) return;

        var mods = keyEvent.modifiers;
        bool ctrl  = (mods & Input.KeyModifier.Control) != 0;
        if (!ctrl) return;

        bool shift = (mods & Input.KeyModifier.Shift) != 0;

        if (shift)
        {
            EditorSceneAssetIO.SaveActiveSceneAsDefaultFolder();
            return;
        }

        // Ctrl+S: strictly Save only; requires currentScenePath to exist
        EditorSceneAssetIO.SaveActiveScene();
    }

    public override void OnRender()
    {
        // Begin ImGui Layout
        ImGuiHost.BeginLayout(Time.renderDeltaTime);
        ImGuizmo.BeginFrame();
        
        // DockSpace
        ImGuiNet.DockSpaceOverViewport(ImGuiNet.GetMainViewport().ID);

        // Play/Pause/Stop overlay
        EditorPlayBar.Draw();

        // Layout GUI
        EditorGUILayout.BeginFrame();
        EditorManager.DrawPanels();
        EditorGUILayout.EndFrame();
        
        // End ImGui Layout
        ImGuiHost.EndLayout();
    }

    public override void OnUpdate()
    {
        // Run scene simulation only in Play mode.
        EditorRuntimeController.Update();
    }

    public override void OnDetach()
    {
        ImGuiHost.DisposeImpl();
    }
}