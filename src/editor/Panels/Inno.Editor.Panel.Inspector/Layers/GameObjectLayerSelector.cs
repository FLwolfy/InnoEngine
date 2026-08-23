using System;
using System.Collections.Generic;

using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the project layer picker used by GameObject target headers.
/// </summary>
internal sealed class GameObjectLayerSelector
{
    private readonly GameLayerSettingsModule m_settings;
    private readonly SceneEdits m_edits;

    /// <summary>
    /// Creates a layer selector backed by the canonical project settings asset.
    /// </summary>
    /// <param name="settings">The project layer-settings module.</param>
    /// <param name="edits">The Scene editing service used to record GameObject layer changes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> or <paramref name="edits"/> is <see langword="null"/>.
    /// </exception>
    internal GameObjectLayerSelector(GameLayerSettingsModule settings, SceneEdits edits)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    /// <summary>
    /// Draws a compact layer selector with an explicit width.
    /// </summary>
    /// <param name="target">The GameObject whose layer should be displayed and edited.</param>
    /// <param name="width">The width reserved for the combo control.</param>
    internal void Draw(GameObject target, float width)
    {
        ArgumentNullException.ThrowIfNull(target);
        LayerStack stack = m_settings.settings.layerStack;
        string preview = stack.GetName(target.layer) ?? $"Layer {target.layer.index} (Undefined)";
        NativeImGui.TextUnformatted("Layer");
        NativeImGui.SameLine(0f, ImGuiWidget.style.inspectorHeaderControlSpacing);
        NativeImGui.SetNextItemWidth(MathF.Max(1f, width));
        if (!NativeImGui.BeginCombo(
                $"##game_object_layer_{target.identity.persistentId:N}",
                preview,
                ImGuiComboFlags.None))
        {
            return;
        }

        IReadOnlyList<LayerDefinition> definitions = stack.GetDefinitions();
        for (int i = 0; i < definitions.Count; i++)
        {
            LayerDefinition definition = definitions[i];
            string label = $"{definition.name} ({definition.layer.index})";
            if (NativeImGui.Selectable(label, definition.layer == target.layer))
                m_edits.SetGameObjectLayer(target, definition.layer);
        }
        NativeImGui.EndCombo();
    }
}
