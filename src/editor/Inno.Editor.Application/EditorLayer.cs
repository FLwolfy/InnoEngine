using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Platform.ImGui;

namespace Inno.Editor.Application;

/// <summary>Bridges the engine layer lifecycle to the attribute-discovered editor runtime.</summary>
internal sealed class EditorLayer : Layer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorRuntime m_runtime;

    internal EditorLayer(PlatformImGuiContext imgui, string projectDirectory)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        ImGuiWidget.SetupStyle();
        m_runtime = new EditorRuntime(projectDirectory);
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
        m_runtime.Update(deltaTime, Time.time, isFocused);
        _ = m_imgui.RenderFrame(m_runtime.Draw);
    }
}
