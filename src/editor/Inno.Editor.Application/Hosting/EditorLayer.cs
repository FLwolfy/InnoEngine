using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.PlayMode;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Platform.Sdl3.ImGui;

namespace Inno.Editor.Application;

/// <summary>
/// Bridges the engine layer lifecycle to the attribute-discovered editor runtime.
/// </summary>
internal sealed class EditorLayer : EditorHostLayer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorContext m_context;
    private readonly EditorProjectDiagnosticPublisher m_diagnostics = new();
    private readonly EditorPlayModeLoop m_playModeLoop = new();
    private readonly ImGuiEditorRuntime m_runtime;
    private readonly Logger m_log;
    private bool m_isShutdownPrepared;
    private float m_totalTime;
    private double m_nextPersistenceRetryTime;

    internal EditorLayer(
        PlatformImGuiContext imgui,
        EditorContext context,
        TypeCatalog types,
        LogRouter logs,
        System.Collections.Generic.IEnumerable<object>? hostServices = null)
    {
        m_imgui = imgui;
        m_context = context;
        m_log = logs.CreateLogger<EditorLayer>();
        EditorWidget.SetupStyle();
        m_runtime = new ImGuiEditorRuntime(
            context,
            types,
            logs,
            CreateHostServices(hostServices, m_playModeLoop));
    }

    internal int panelCount => m_runtime.panelCount;

    internal bool isFocused { get; set; }

    internal float totalTime
    {
        set => m_totalTime = value;
    }

    internal void DisposeUnattached()
    {
        m_runtime.Dispose();
        m_diagnostics.Dispose();
    }

    internal override void Attach()
    {
        _ = Listen<KeyPressedEvent>(HandleKeyPressed, priority: 1000);
        m_runtime.Start();
    }

    internal override void Detach()
    {
        PrepareShutdown();
        m_runtime.Dispose();
        m_diagnostics.Dispose();
    }

    /// <summary>
    /// Advances this feature using the current runtime state.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    internal override void Update(float deltaTime)
        => m_playModeLoop.Tick(deltaTime);

    internal override void LateUpdate(float deltaTime)
    {
        using (m_playModeLoop.EnterPresentationScope())
            m_runtime.Update(new EditorFrame(deltaTime, m_totalTime, isFocused));

        using (m_playModeLoop.EnterPresentationScope())
        {
            _ = m_imgui.RenderFrame(m_runtime.Draw);
            SaveLayoutIfChanged();
        }
    }

    private void HandleKeyPressed(KeyPressedEvent keyEvent)
    {
        using (m_playModeLoop.EnterPresentationScope())
            m_runtime.HandleKeyPressed(keyEvent);
    }

    internal bool PrepareShutdown()
    {
        if (m_isShutdownPrepared)
            return true;
        try
        {
            // Extension-state capture freezes its periodic writer before reading module state. Layout is
            // captured afterwards while the ImGui context and all panels are still alive.
            m_runtime.PrepareShutdown();
            CaptureLayout(force: true);
            m_context.SaveLayout();
            m_diagnostics.ResolvePersistence();
            m_isShutdownPrepared = true;
            return true;
        }
        catch (Exception exception)
        {
            if (m_diagnostics.PublishPersistenceFailure(exception))
            {
                m_log.Write(
                    LogLevel.Error,
                    "Project editor state could not be saved to '{0}': {1}",
                    [m_context.layoutPath, exception]);
            }
            return false;
        }
    }

    private void SaveLayoutIfChanged()
    {
        bool layoutChanged = CaptureLayout(force: false);
        if (!layoutChanged &&
            (!m_diagnostics.hasPersistenceFailure ||
             m_context.frame.totalTime < m_nextPersistenceRetryTime))
        {
            return;
        }
        try
        {
            _ = m_context.SaveLayoutIfChanged();
            m_diagnostics.ResolvePersistence();
            m_nextPersistenceRetryTime = 0;
        }
        catch (Exception exception)
        {
            m_nextPersistenceRetryTime = m_context.frame.totalTime + 1.0;
            if (m_diagnostics.PublishPersistenceFailure(exception))
            {
                m_log.Write(
                    LogLevel.Error,
                    "Project editor state could not be saved to '{0}': {1}",
                    [m_context.layoutPath, exception]);
            }
        }
    }

    private bool CaptureLayout(bool force)
    {
        if (!m_imgui.TryCaptureIniSettings(out string layout, force))
            return false;
        m_context.SetImGuiLayout(layout);
        return true;
    }

    private static IEnumerable<object> CreateHostServices(
        IEnumerable<object>? hostServices,
        EditorPlayModeLoop playModeLoop)
    {
        List<object> services = hostServices is null ? [] : new List<object>(hostServices);
        services.Add(playModeLoop);
        return services;
    }
}
