using System;

using Inno.Core.Settings;
using Inno.Editor.Core;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Publishes isolated current-generation Scene classification settings and assignment diagnostics.
/// </summary>
[EditorModule("scene-project-settings", order: 205)]
internal sealed class SceneProjectSettingsModule : EditorModule
{
    private readonly GameLayerDiagnosticPublisher m_layerDiagnostics = new();
    private readonly GameTagDiagnosticPublisher m_tagDiagnostics = new();
    private GameLayerStack? m_layerStack;
    private GameTagCatalog? m_tagCatalog;
    private long m_observedRevision = -1;

    /// <summary>Gets the effective project layer catalog snapshot.</summary>
    /// <exception cref="InvalidOperationException">Thrown before the module has started.</exception>
    internal GameLayerStack layerStack => m_layerStack
        ?? throw new InvalidOperationException("The project layer settings are not available.");

    /// <summary>Gets the effective project tag catalog snapshot.</summary>
    /// <exception cref="InvalidOperationException">Thrown before the module has started.</exception>
    internal GameTagCatalog tagCatalog => m_tagCatalog
        ?? throw new InvalidOperationException("The project tag settings are not available.");

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
        => RefreshSettings();

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (m_observedRevision != ProjectSettingsManager.revision)
            RefreshSettings();
        m_layerDiagnostics.Refresh(layerStack);
        m_tagDiagnostics.Refresh(tagCatalog);
    }

    /// <inheritdoc />
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
        m_layerStack = ProjectSettingsManager.Get<GameLayerStack>(GameLayerStack.settingId);
        m_tagCatalog = ProjectSettingsManager.Get<GameTagCatalog>(GameTagCatalog.settingId);
        m_observedRevision = ProjectSettingsManager.revision;
    }
}
