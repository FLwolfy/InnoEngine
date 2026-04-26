using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Panels;
using Inno.Platform.ImGui;
using Inno.Core.Framework;

namespace Inno.Editor.Application;

internal sealed class EditorLayer : Layer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorContext m_context = new();
    private readonly EditorPanelRegistry m_registry = new();

    internal EditorLayer(PlatformImGuiContext imgui)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        ImGuiWidget.SetupStyle();
    }

    public override void OnAttach()
    {
        m_context.Attach();
        IEditorPanel[] panels = EditorDefaultPanels.Create();
        for (int i = 0; i < panels.Length; i++)
        {
            m_registry.Register(panels[i], m_context);
        }
    }

    public override void OnDetach()
    {
        m_registry.Clear(m_context);
        m_context.Detach();
    }

    public override void OnLateUpdate(float deltaTime)
    {
        m_context.frameDeltaTime = deltaTime;
        m_context.totalTime = Time.time;

        _ = m_imgui.RenderFrame(() =>
        {
            ImGuiWidget.DockSpaceFullscreen();
            DrawMainMenu();
            DrawPanels();
        });
    }   

    private void DrawMainMenu()
    {
        List<(string title, bool isOpen)> viewItems = new(m_registry.count);
        for (int i = 0; i < m_registry.count; i++)
        {
            IEditorPanel panel = m_registry.panels[i];
            viewItems.Add((panel.title, panel.isOpen));
        }

        ImGuiWidget.ViewMenu(viewItems, (index, isOpen) =>
        {
            if (index < 0 || index >= m_registry.count)
                return;

            m_registry.panels[index].isOpen = isOpen;
        });
    }

    private void DrawPanels()
    {
        for (int i = 0; i < m_registry.count; i++)
        {
            IEditorPanel panel = m_registry.panels[i];
            bool isOpen = panel.isOpen;
            ImGuiWidget.PanelWindow(panel.title, ref isOpen, () => panel.OnRender(m_context));
            panel.isOpen = isOpen;
        }
    }
}
