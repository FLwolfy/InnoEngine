using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Layers;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws and persists the project layer catalog and interaction matrix.
/// </summary>
[InspectionDrawer(typeof(GameLayerSettingsAsset))]
internal sealed class GameLayerSettingsInspectionDrawer : InspectionDrawer<GameLayerSettingsAsset>
{
    private const nuint C_LAYER_NAME_BUFFER_SIZE = 128;

    private readonly GameLayerSettingsModule m_settings;
    private readonly string[] m_nameBuffers = new string[Layer.C_MAX_COUNT];
    private string m_error = string.Empty;
    private bool m_initialized;
    private long m_contentVersion = -1;

    /// <summary>
    /// Creates a project layer settings drawer.
    /// </summary>
    /// <param name="settings">The module that owns and saves the canonical settings asset.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    internal GameLayerSettingsInspectionDrawer(GameLayerSettingsModule settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public override string icon => ImGuiIcon.LayerGroup;

    /// <inheritdoc />
    protected override string GetName(InspectionDrawContext context, GameLayerSettingsAsset target)
        => "Game Layers";

    /// <inheritdoc />
    protected override void DrawHeader(InspectionDrawContext context, GameLayerSettingsAsset target)
        => ImGuiWidget.ColoredText(EditorPalette.assetBreadcrumbText, target.sourcePath);

    /// <inheritdoc />
    protected override void Draw(InspectionDrawContext context, GameLayerSettingsAsset target)
    {
        if (!ReferenceEquals(target, m_settings.settings))
            target = m_settings.settings;
        SynchronizeBuffers(target);
        NativeImGui.TextUnformatted("Layers");
        NativeImGui.Separator();
        DrawLayerRows(target);
        NativeImGui.Spacing();
        NativeImGui.TextUnformatted("Interactions");
        NativeImGui.Separator();
        DrawInteractionRows(target);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    private void DrawLayerRows(GameLayerSettingsAsset target)
    {
        LayerStack stack = target.layerStack;
        float indexWidth = NativeImGui.CalcTextSize("31").X;
        for (int index = 0; index < Layer.C_MAX_COUNT; index++)
        {
            var layer = new Layer(index);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            NativeImGui.SameLine(indexWidth + NativeImGui.GetStyle().ItemSpacing.X);
            if (layer == Layer.defaultLayer)
            {
                NativeImGui.TextUnformatted(stack.GetName(layer) ?? "Default");
                continue;
            }

            NativeImGui.SetNextItemWidth(-1f);
            string value = m_nameBuffers[index];
            if (!NativeImGui.InputTextWithHint(
                    $"##layer_name_{index}",
                    "Unused",
                    ref value,
                    C_LAYER_NAME_BUFFER_SIZE,
                    ImGuiInputTextFlags.EnterReturnsTrue))
            {
                m_nameBuffers[index] = value;
                continue;
            }
            m_nameBuffers[index] = value;
            CommitLayerName(target, layer, value);
        }
    }

    private void DrawInteractionRows(GameLayerSettingsAsset target)
    {
        LayerStack stack = target.layerStack;
        IReadOnlyList<LayerDefinition> definitions = stack.GetDefinitions();
        for (int sourceIndex = 0; sourceIndex < definitions.Count; sourceIndex++)
        {
            LayerDefinition source = definitions[sourceIndex];
            string preview = GetInteractionPreview(stack, source.layer, definitions);
            NativeImGui.SetNextItemWidth(-1f);
            if (!NativeImGui.BeginCombo(
                    $"{source.name}##layer_interactions_{source.layer.index}",
                    preview,
                    ImGuiComboFlags.None))
            {
                continue;
            }

            for (int targetIndex = 0; targetIndex < definitions.Count; targetIndex++)
            {
                LayerDefinition candidate = definitions[targetIndex];
                bool enabled = stack.CanInteract(source.layer, candidate.layer);
                if (!NativeImGui.Checkbox(
                        $"{candidate.name}##layer_pair_{source.layer.index}_{candidate.layer.index}",
                        ref enabled))
                {
                    continue;
                }
                stack.SetInteraction(source.layer, candidate.layer, enabled);
                Save(target);
            }
            NativeImGui.EndCombo();
        }
    }

    private void CommitLayerName(GameLayerSettingsAsset target, Layer layer, string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
                _ = target.layerStack.Remove(layer);
            else
                target.layerStack.Define(layer, value);
            Save(target);
            m_nameBuffers[layer.index] = target.layerStack.GetName(layer) ?? string.Empty;
        }
        catch (Exception exception)
        {
            m_error = exception.Message;
            m_nameBuffers[layer.index] = target.layerStack.GetName(layer) ?? string.Empty;
        }
    }

    private void Save(GameLayerSettingsAsset target)
    {
        if (!ReferenceEquals(target, m_settings.settings))
            throw new InvalidOperationException("Only the canonical project layer settings can be edited.");
        m_settings.Save();
        m_error = string.Empty;
    }

    private void SynchronizeBuffers(GameLayerSettingsAsset target)
    {
        if (m_initialized && m_contentVersion == target.contentVersion)
            return;
        for (int index = 0; index < Layer.C_MAX_COUNT; index++)
            m_nameBuffers[index] = target.layerStack.GetName(new Layer(index)) ?? string.Empty;
        m_initialized = true;
        m_contentVersion = target.contentVersion;
    }

    private static string GetInteractionPreview(
        LayerStack stack,
        Layer source,
        IReadOnlyList<LayerDefinition> definitions)
    {
        int enabled = 0;
        for (int i = 0; i < definitions.Count; i++)
        {
            if (stack.CanInteract(source, definitions[i].layer))
                enabled++;
        }
        return enabled == definitions.Count ? "Everything" : $"{enabled} of {definitions.Count}";
    }
}
