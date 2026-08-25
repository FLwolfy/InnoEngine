using System;

using Inno.Editor.Core;
using Inno.Editor.Settings;
using Inno.Engine.Scene.Layers;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Publishes layer diagnostics from the project Settings layer catalog.
/// </summary>
[EditorModule("game-layer-settings", order: 205)]
internal sealed class GameLayerSettingsModule(EditorSettings settings) : EditorModule
{
    private readonly GameLayerDiagnosticPublisher m_diagnostics = new();
    private GameLayerStack? m_layerStack;

    /// <summary>
    /// Gets the current path-addressed project layer catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown before the module has started.
    /// </exception>
    internal GameLayerStack layerStack => m_layerStack
        ?? throw new InvalidOperationException("The project layer Settings are not available.");

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        m_layerStack = GameLayersSetting.CreateStack(settings.Get("Project/Layers/Game Layers"));
        settings.changed += OnSettingChanged;
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
        => m_diagnostics.Refresh(layerStack);

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        settings.changed -= OnSettingChanged;
        m_diagnostics.Clear();
        m_layerStack = null;
    }

    private void OnSettingChanged(EditorSettings changedSettings)
        => m_layerStack = GameLayersSetting.CreateStack(
            changedSettings.Get("Project/Layers/Game Layers"));
}
