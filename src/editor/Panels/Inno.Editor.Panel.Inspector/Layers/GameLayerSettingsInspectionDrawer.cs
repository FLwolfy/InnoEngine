using System;
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
/// Draws and persists the project layer catalog.
/// </summary>
[InspectionDrawer(typeof(GameLayerSettingsAsset))]
internal sealed class GameLayerSettingsInspectionDrawer : InspectionDrawer<GameLayerSettingsAsset>
{
    private const nuint C_LAYER_NAME_BUFFER_SIZE = 128;

    private readonly GameLayerSettingsModule m_settings;
    private readonly string[] m_nameBuffers = new string[GameLayer.C_MAX_COUNT];
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
    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        GameLayerSettingsAsset target)
        => ("Game Layers", null);

    /// <inheritdoc />
    protected override void DrawHeader(InspectionDrawContext context, GameLayerSettingsAsset target)
        => ImGuiWidget.ColoredText(EditorPalette.assetBreadcrumbText, target.sourcePath);

    /// <inheritdoc />
    protected override void Draw(InspectionDrawContext context, GameLayerSettingsAsset target)
    {
        if (!m_settings.IsCanonical(target))
        {
            ImGuiWidget.ColoredText(
                EditorPalette.error,
                $"Game layer settings are active only at '{GameLayerSettingsAsset.defaultPath}'.");
            return;
        }
        target = m_settings.settings;
        SynchronizeBuffers(target);
        NativeImGui.TextUnformatted("Layers");
        NativeImGui.Separator();
        DrawLayerRows(target);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    private void DrawLayerRows(GameLayerSettingsAsset target)
    {
        GameLayerStack stack = target.layerStack;
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoSavedSettings;
        if (!NativeImGui.BeginTable("##game_layer_definitions", 2, flags))
            return;
        NativeImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed);
        NativeImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            var layer = new GameLayer(index);
            NativeImGui.TableNextRow();
            _ = NativeImGui.TableSetColumnIndex(0);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _ = NativeImGui.TableSetColumnIndex(1);
            if (layer == GameLayer.defaultLayer)
            {
                NativeImGui.TextUnformatted(stack.GetName(layer) ?? "Default");
                continue;
            }

            NativeImGui.SetNextItemWidth(-1f);
            string value = m_nameBuffers[index];
            bool submitted = NativeImGui.InputTextWithHint(
                    $"##layer_name_{index}",
                    "Unused",
                    ref value,
                    C_LAYER_NAME_BUFFER_SIZE,
                    ImGuiInputTextFlags.EnterReturnsTrue);
            m_nameBuffers[index] = value;
            if (submitted || NativeImGui.IsItemDeactivatedAfterEdit())
                CommitLayerName(target, layer, value);
        }
        NativeImGui.EndTable();
    }

    private void CommitLayerName(GameLayerSettingsAsset target, GameLayer layer, string value)
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
        if (!m_settings.IsCanonical(target))
            throw new InvalidOperationException("Only the canonical project layer settings can be edited.");
        m_settings.Save();
        m_error = string.Empty;
    }

    private void SynchronizeBuffers(GameLayerSettingsAsset target)
    {
        if (m_initialized && m_contentVersion == target.contentVersion)
            return;
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            m_nameBuffers[index] = target.layerStack.GetName(new GameLayer(index)) ?? string.Empty;
        m_initialized = true;
        m_contentVersion = target.contentVersion;
    }
}
