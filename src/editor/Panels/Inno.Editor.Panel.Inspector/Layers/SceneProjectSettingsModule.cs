using System;

using Inno.Core.Diagnostics;
using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Scene;
using Inno.Scene.Layers;
using Inno.Runtime;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Publishes isolated current-generation Scene classification settings and assignment diagnostics.
/// </summary>
[EditorModule("scene-project-settings", order: 205)]
internal sealed class SceneProjectSettingsModule : EditorModule
{
    private readonly ProjectSettingsStore m_settings;
    private readonly GameLayerDiagnosticPublisher m_layerDiagnostics;
    private readonly GameTagDiagnosticPublisher m_tagDiagnostics;
    private GameLayerStack? m_layerStack;
    private GameTagCatalog? m_tagCatalog;
    private long m_observedRevision = -1;

    internal SceneProjectSettingsModule(
        ProjectSettingsStore settings,
        RuntimeSession runtimeSession,
        DiagnosticHub diagnostics)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(runtimeSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        m_layerDiagnostics = new GameLayerDiagnosticPublisher(runtimeSession.scenes, diagnostics);
        m_tagDiagnostics = new GameTagDiagnosticPublisher(runtimeSession.scenes, diagnostics);
    }

    /// <summary>
    /// Gets the effective project layer catalog snapshot.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown before the module has started.
    /// </exception>
    internal GameLayerStack layerStack => m_layerStack
        ?? throw new InvalidOperationException("The project layer settings are not available.");

    /// <summary>
    /// Gets the effective project tag catalog snapshot.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown before the module has started.
    /// </exception>
    internal GameTagCatalog tagCatalog => m_tagCatalog
        ?? throw new InvalidOperationException("The project tag settings are not available.");

    /// <summary>
    /// Initializes this feature when its owning runtime becomes active.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStart(EditorContext context)
        => RefreshSettings();

    /// <summary>
    /// Advances this feature using the current runtime state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnUpdate(EditorContext context)
    {
        if (m_observedRevision != m_settings.revision)
            RefreshSettings();
        m_layerDiagnostics.Refresh(layerStack);
        m_tagDiagnostics.Refresh(tagCatalog);
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        m_layerDiagnostics.Clear();
        m_tagDiagnostics.Clear();
        m_layerStack = null;
        m_tagCatalog = null;
        m_observedRevision = -1;
    }

    private void RefreshSettings()
    {
        m_layerStack = m_settings.Get<GameLayerStack>(GameLayerStack.settingId);
        m_tagCatalog = m_settings.Get<GameTagCatalog>(GameTagCatalog.settingId);
        m_observedRevision = m_settings.revision;
    }
}
