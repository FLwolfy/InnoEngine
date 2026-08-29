using System;
using System.Collections.Generic;

using Inno.Editor.ImGui;
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
    private readonly SceneProjectSettingsModule m_settings;
    private readonly SceneEdits m_edits;

    /// <summary>
    /// Creates a layer selector backed by the project Settings layer catalog.
    /// </summary>
    /// <param name="settings">The project Scene-classification settings module.</param>
    /// <param name="edits">The Scene editing service used to record GameObject layer changes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> or <paramref name="edits"/> is <see langword="null"/>.
    /// </exception>
    internal GameObjectLayerSelector(SceneProjectSettingsModule settings, SceneEdits edits)
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
        GameLayerStack stack = m_settings.layerStack;
        string preview = FormatLayerPreview(stack.GetName(target.layer));
        ImGuiWidget.LabelChip("Layer", EditorPalette.inspectorLayerLabel);
        NativeImGui.SameLine(0f, 0f);
        NativeImGui.SetNextItemWidth(MathF.Max(1f, width));
        if (!NativeImGui.BeginCombo(
                $"##game_object_layer_{target.identity.persistentId:N}",
                preview,
                ImGuiComboFlags.None))
        {
            return;
        }

        try
        {
            IReadOnlyList<GameLayerDefinition> definitions = stack.GetDefinitions();
            for (int i = 0; i < definitions.Count; i++)
            {
                GameLayerDefinition definition = definitions[i];
                string label = FormatLayerLabel(definition.layer, definition.name);
                if (NativeImGui.Selectable(label, definition.layer == target.layer))
                    m_edits.SetGameObjectLayer(target, definition.layer);
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    /// <summary>
    /// Determines whether the current project catalog defines a layer slot.
    /// </summary>
    /// <param name="layer">The layer slot to resolve.</param>
    /// <returns><see langword="true"/> when the layer can be selected.</returns>
    internal bool IsLayerDefined(GameLayer layer)
        => m_settings.layerStack.IsDefined(layer);

    private static string FormatLayerLabel(GameLayer layer, string name)
        => $"({layer.index}) {name}";

    private static string FormatLayerPreview(string? name)
        => name ?? "Undefined";
}
