using System;

using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Platform.ImGui;

namespace Inno.Editor.Application;

/// <summary>Bridges the engine layer lifecycle to the attribute-discovered editor runtime.</summary>
internal sealed class EditorLayer : Layer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorContext m_context;
    private readonly ImGuiEditorRuntime m_runtime;
    private bool m_isShutdownPrepared;

    internal EditorLayer(PlatformImGuiContext imgui, EditorContext context)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        m_context = context;
        EditorWidget.SetupStyle();
        m_runtime = new ImGuiEditorRuntime(context);
    }

    internal int panelCount => m_runtime.panelCount;

    internal bool isFocused { get; set; }

    /// <inheritdoc />
    public override void OnAttach()
    {
        _ = Listen<KeyPressedEvent>(m_runtime.HandleKeyPressed, priority: 1000);
        m_runtime.Start();
    }

    /// <inheritdoc />
    public override void OnDetach()
    {
        PrepareShutdown();
        m_runtime.Dispose();
    }

    /// <inheritdoc />
    public override void OnLateUpdate(float deltaTime)
    {
        m_runtime.Update(new EditorFrame(deltaTime, Time.time, isFocused));
        _ = m_imgui.RenderFrame(m_runtime.Draw);
        SaveLayoutIfChanged();
    }

    internal bool PrepareShutdown()
    {
        if (m_isShutdownPrepared)
            return true;
        try
        {
            // Workspace capture freezes its periodic writer before reading module state. Layout is
            // captured afterwards while the ImGui context and all panels are still alive.
            m_runtime.PrepareShutdown();
            CaptureLayout(force: true);
            m_context.settings.Save();
            m_isShutdownPrepared = true;
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Project editor state could not be saved to '{0}': {1}",
                m_context.settings.path,
                exception);
            return false;
        }
    }

    private void SaveLayoutIfChanged()
    {
        if (!CaptureLayout(force: false))
            return;
        _ = m_context.settings.SaveIfChanged();
    }

    private bool CaptureLayout(bool force)
    {
        if (!m_imgui.TryCaptureIniSettings(out string layout, force))
            return false;
        m_context.settings.SetImGuiLayout(layout);
        return true;
    }
}
