using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

using Inno.Core.Framework;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.Panels;
using Inno.Editor.Scripting;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Application;

internal sealed class EditorLayer : Layer
{
    private const string C_SCRIPT_COMPILATION_POPUP = "Compiling Scripts##script_compilation";
    private const double C_SCRIPT_POPUP_FADE_IN_SECONDS = 0.12;
    private const double C_SCRIPT_POPUP_MINIMUM_VISIBLE_SECONDS = 0.35;
    private const double C_SCRIPT_POPUP_FADE_OUT_SECONDS = 0.14;
    private const float C_SCRIPT_POPUP_WIDTH = 460f;

    private readonly PlatformImGuiContext m_imgui;
    private readonly ScriptManager m_scriptManager;
    private readonly EditorContext m_context = new();
    private readonly EditorPanelRegistry m_panelRegistry = new();
    private readonly Stopwatch m_uiTimer = Stopwatch.StartNew();

    private double m_scriptPopupVisibleAt;
    private double m_scriptPopupHideAt;
    private bool m_isScriptCompilationActive;
    private bool m_isScriptPopupVisible;

    internal EditorLayer(PlatformImGuiContext imgui, ScriptManager scriptManager)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        m_scriptManager = scriptManager;
        ImGuiWidget.SetupStyle();
    }

    internal void BeginScriptCompilation()
    {
        if (m_isScriptCompilationActive)
            return;
        m_isScriptCompilationActive = true;
        if (m_isScriptPopupVisible)
        {
            m_scriptPopupHideAt = double.PositiveInfinity;
            return;
        }
        m_isScriptPopupVisible = true;
        m_scriptPopupVisibleAt = m_uiTimer.Elapsed.TotalSeconds;
        m_scriptPopupHideAt = double.PositiveInfinity;
    }

    internal void EndScriptCompilation()
    {
        if (!m_isScriptCompilationActive)
            return;
        m_isScriptCompilationActive = false;
        if (m_isScriptPopupVisible)
        {
            double now = m_uiTimer.Elapsed.TotalSeconds;
            m_scriptPopupHideAt = Math.Max(
                now,
                m_scriptPopupVisibleAt + C_SCRIPT_POPUP_MINIMUM_VISIBLE_SECONDS);
        }
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
            bool blocksInteraction = IsScriptCompilationBlocking();
            if (blocksInteraction)
                NativeImGui.BeginDisabled(true);
            DrawMainMenu();
            DrawPanels();
            if (blocksInteraction)
                NativeImGui.EndDisabled();
            DrawScriptCompilationModal();
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

    private void DrawScriptCompilationModal()
    {
        double now = m_uiTimer.Elapsed.TotalSeconds;
        if (!m_isScriptPopupVisible)
            return;
        if (!m_isScriptCompilationActive && now >= m_scriptPopupHideAt + C_SCRIPT_POPUP_FADE_OUT_SECONDS)
        {
            CloseScriptCompilationPopup();
            return;
        }

        NativeImGui.OpenPopup(C_SCRIPT_COMPILATION_POPUP, ImGuiPopupFlags.NoReopen);
        float alpha = GetScriptPopupAlpha(now);
        NativeImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        NativeImGui.SetNextWindowSize(new Vector2(C_SCRIPT_POPUP_WIDTH, 0f), ImGuiCond.Always);
        ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoResize;
        if (!NativeImGui.BeginPopupModal(C_SCRIPT_COMPILATION_POPUP, flags))
        {
            NativeImGui.PopStyleVar();
            return;
        }

        float progress = m_scriptManager.compilationProgress;
        NativeImGui.TextUnformatted(m_scriptManager.compilationStatus);
        NativeImGui.ProgressBar(progress, new Vector2(-1f, 0f), $"{progress:P0}");
        NativeImGui.EndPopup();
        NativeImGui.PopStyleVar();
    }

    private bool IsScriptCompilationBlocking()
    {
        if (m_isScriptCompilationActive)
            return true;
        return m_isScriptPopupVisible &&
               m_uiTimer.Elapsed.TotalSeconds < m_scriptPopupHideAt + C_SCRIPT_POPUP_FADE_OUT_SECONDS;
    }

    private float GetScriptPopupAlpha(double now)
    {
        if (m_isScriptCompilationActive || now <= m_scriptPopupHideAt)
        {
            double fadeIn = (now - m_scriptPopupVisibleAt) / C_SCRIPT_POPUP_FADE_IN_SECONDS;
            return (float)Math.Clamp(fadeIn, 0.05, 1.0);
        }
        double fadeOut = (now - m_scriptPopupHideAt) / C_SCRIPT_POPUP_FADE_OUT_SECONDS;
        return (float)Math.Clamp(1.0 - fadeOut, 0.05, 1.0);
    }

    private void CloseScriptCompilationPopup()
    {
        if (NativeImGui.IsPopupOpen(C_SCRIPT_COMPILATION_POPUP) &&
            NativeImGui.BeginPopupModal(C_SCRIPT_COMPILATION_POPUP))
        {
            NativeImGui.CloseCurrentPopup();
            NativeImGui.EndPopup();
        }
        m_isScriptPopupVisible = false;
    }
}
