using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Panels;
using Inno.Platform.ImGui;
using Inno.Core.Framework;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Application;

internal sealed class EditorLayer : Layer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorContext m_context = new();
    private readonly EditorPanelRegistry m_panelRegistry = new();

    internal EditorLayer(PlatformImGuiContext imgui)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        ImGuiWidget.SetupStyle();
    }

    public override void OnAttach()
    {
        m_context.Attach();
        EditorPanel[] panels = EditorDefaultPanels.Create();
        for (int i = 0; i < panels.Length; i++)
        {
            m_panelRegistry.Register(panels[i], m_context);
        }
    }

    public override void OnDetach()
    {
        m_panelRegistry.Clear(m_context);
        m_context.Detach();
    }

    public override void OnLateUpdate(float deltaTime)
    {
        m_context.frameDeltaTime = deltaTime;
        m_context.totalTime = Time.time;

        _ = m_imgui.RenderFrame(() =>
        {
            _ = NativeImGui.DockSpaceOverViewport();
            DrawMainMenu();
            DrawPanels();
        });
    }   

    private void DrawMainMenu()
    {
        List<(string title, bool isOpen)> viewItems = new(m_panelRegistry.count);
        for (int i = 0; i < m_panelRegistry.count; i++)
        {
            EditorPanel panel = m_panelRegistry.panels[i];
            viewItems.Add((panel.title, panel.isOpen));
        }

        ImGuiWidget.ViewMenu(viewItems, (index, isOpen) =>
        {
            if (index < 0 || index >= m_panelRegistry.count)
                return;

            m_panelRegistry.panels[index].isOpen = isOpen;
        });
    }

    private void DrawPanels()
    {
        for (int i = 0; i < m_panelRegistry.count; i++)
        {
            EditorPanel panel = m_panelRegistry.panels[i];
            bool isOpen = panel.isOpen;
            ImGuiWidget.PanelWindow(panel.title, ref isOpen, () => panel.OnRender(m_context));
            panel.isOpen = isOpen;
        }
    }
}
