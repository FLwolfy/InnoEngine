using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Framework;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.PlayMode;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Platform.ImGui;

namespace Inno.Editor.Application;

/// <summary>Bridges the engine layer lifecycle to the attribute-discovered editor runtime.</summary>
internal sealed class EditorLayer : Layer
{
    private readonly PlatformImGuiContext m_imgui;
    private readonly EditorContext m_context;
    private readonly EditorProjectDiagnosticPublisher m_diagnostics = new();
    private readonly EditorPlayModeLoop m_playModeLoop = new();
    private readonly ImGuiEditorRuntime m_runtime;
    private bool m_isShutdownPrepared;
    private double m_nextPersistenceRetryTime;

    internal EditorLayer(
        PlatformImGuiContext imgui,
        EditorContext context,
        System.Collections.Generic.IEnumerable<object>? hostServices = null)
        : base("EditorLayer")
    {
        m_imgui = imgui;
        m_context = context;
        EditorWidget.SetupStyle();
        m_runtime = new ImGuiEditorRuntime(context, CreateHostServices(hostServices, m_playModeLoop));
    }

    internal int panelCount => m_runtime.panelCount;

    internal bool isFocused { get; set; }

    internal void DisposeUnattached()
    {
        m_runtime.Dispose();
        m_diagnostics.Dispose();
    }

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
        m_diagnostics.Dispose();
    }

    /// <inheritdoc />
    public override void OnFixedUpdate(float fixedDeltaTime)
        => m_playModeLoop.FixedUpdate(fixedDeltaTime);

    /// <inheritdoc />
    public override void OnUpdate(float deltaTime)
        => m_playModeLoop.Update(deltaTime);

    /// <inheritdoc />
    public override void OnLateUpdate(float deltaTime)
    {
        // Editor transitions run after every simulation phase so a new Play Mode session starts on a full frame.
        m_playModeLoop.LateUpdate(deltaTime);
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
                Log.Error("Project editor state could not be saved to '{0}': {1}",
                    m_context.layoutPath,
                    exception);
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
                Log.Error("Project editor state could not be saved to '{0}': {1}",
                    m_context.layoutPath,
                    exception);
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
