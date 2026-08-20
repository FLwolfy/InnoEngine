using Inno.Core.Events;
using Inno.Core.Framework;
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
    private readonly ImGuiEditorRuntime m_runtime;

    internal EditorLayer(PlatformImGuiContext imgui, string projectDirectory)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        EditorWidget.SetupStyle();
        m_runtime = new ImGuiEditorRuntime(projectDirectory);
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
    public override void OnDetach() => m_runtime.Dispose();

    /// <inheritdoc />
    public override void OnLateUpdate(float deltaTime)
    {
        m_runtime.Update(new EditorFrame(deltaTime, Time.time, isFocused));
        _ = m_imgui.RenderFrame(m_runtime.Draw);
    }
}
