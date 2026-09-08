using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Layers;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Adapter.Presentation;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.PlayMode;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Application;

/// <summary>
/// Bridges the engine layer lifecycle to the attribute-discovered editor runtime.
/// </summary>
internal sealed class EditorLayer : Layer
{
    private readonly IPresentationContext m_presentation;
    private readonly EditorContext m_context;
    private readonly EditorProjectDiagnosticPublisher m_diagnostics = new();
    private readonly EditorPlayModeLoop m_playModeLoop = new();
    private readonly ImGuiEditorRuntime m_runtime;
    private readonly Logger m_log;
    private readonly LifetimeScope m_resources = new();
    private bool m_isShutdownPrepared;
    private float m_totalTime;
    private double m_nextPersistenceRetryTime;

    internal EditorLayer(
        IPresentationContext presentation,
        EditorContext context,
        TypeCatalog types,
        LogRouter logs,
        System.Collections.Generic.IEnumerable<object>? hostServices = null)
    {
        m_presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        m_context = context;
        m_log = logs.CreateLogger<EditorLayer>();
        EditorWidget.SetupStyle();
        m_resources.Own(m_diagnostics);
        m_runtime = m_resources.Own(new ImGuiEditorRuntime(
            context,
            types,
            logs,
            CreateHostServices(hostServices, m_playModeLoop)));
    }

    internal int panelCount => m_runtime.panelCount;

    internal bool isFocused { get; set; }

    internal float totalTime
    {
        set => m_totalTime = value;
    }

    internal void DisposeUnattached()
    {
        m_resources.Dispose();
    }

    internal void QuiescePlayMode() => m_playModeLoop.Quiesce();

    /// <summary>
    /// Starts the editor extension runtime and subscribes its host-level input handling.
    /// </summary>
    public override void OnAttach()
    {
        _ = Listen<KeyPressedEvent>(HandleKeyPressed, priority: 1000);
        m_runtime.Start();
    }

    /// <summary>
    /// Saves editor state and releases the active editor extension runtime.
    /// </summary>
    public override void OnDetach()
    {
        PrepareShutdown();
        m_resources.Dispose();
    }

    /// <summary>
    /// Advances this feature using the current runtime state.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public override void OnUpdate(float deltaTime)
        => m_playModeLoop.Tick(deltaTime);

    /// <summary>
    /// Updates editor presentation after the active Play Mode session has advanced.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed editor frame time in seconds.
    /// </param>
    public override void OnLateUpdate(float deltaTime)
    {
        using (m_playModeLoop.EnterPresentationScope())
            m_runtime.Update(new EditorFrame(deltaTime, m_totalTime, isFocused));

        using (m_playModeLoop.EnterPresentationScope())
        {
            m_presentation.RenderFrame(m_runtime.Draw);
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
        if (!m_presentation.TryCaptureLayout(out string layout, force))
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
