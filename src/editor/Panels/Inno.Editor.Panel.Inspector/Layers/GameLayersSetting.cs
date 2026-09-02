using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Inno.Core.Settings;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Scene.Layers;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[ProjectSettingPath("Project/Scene/Layers")]
internal sealed class GameLayersSetting : ProjectSettingEditor<GameLayerStack>
{
    private const nuint C_LAYER_ID_BUFFER_SIZE = 129;
    private const nuint C_LAYER_NAME_BUFFER_SIZE = 128;

    private readonly string[] m_idBuffers = new string[GameLayer.C_MAX_COUNT];
    private readonly string[] m_nameBuffers = new string[GameLayer.C_MAX_COUNT];
    private string m_error = string.Empty;
    private string?[]? m_observedIds;
    private string?[]? m_observedNames;

    /// <summary>
    /// Gets the stable project-setting identity used for discovery and persistence.
    /// </summary>
    public override ProjectSettingId settingId => GameLayerStack.settingId;

    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Definitions";

    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Assign globally stable IDs, display names, and runtime slots to project and Plugin layers.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(GameLayerStack setting)
    {
        string?[] ids = CaptureIds(setting);
        string?[] names = CaptureNames(setting);
        SynchronizeBuffers(ids, names);
        DrawLayerRows(setting, ids, names);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    private void DrawLayerRows(GameLayerStack setting, string?[] ids, string?[] names)
    {
        int definedCount = names.Count(static name => !string.IsNullOrWhiteSpace(name));
        DrawLayerToolbar(setting, names, definedCount);
        NativeImGui.Spacing();

        ImGuiTableFlags flags = ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.BordersInnerH |
                                ImGuiTableFlags.BordersInnerV |
                                ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoSavedSettings;
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, ImGuiWidget.style.cellPadding);
        if (!NativeImGui.BeginTable("##game_layer_definitions", 4, flags))
        {
            NativeImGui.PopStyleVar();
            return;
        }
        try
        {
            NativeImGui.TableSetupColumn(
                "Slot",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                48f * ImGuiWidget.style.zoom);
            NativeImGui.TableSetupColumn(
                "Stable ID",
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize,
                1.2f);
            NativeImGui.TableSetupColumn(
                "Name",
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize,
                0.8f);
            NativeImGui.TableSetupColumn(
                "Action",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                72f * ImGuiWidget.style.zoom);
            DrawLayerTableHeader();
            for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            {
                if (string.IsNullOrWhiteSpace(names[index]))
                    continue;
                DrawLayerRow(setting, ids, names, index);
            }
        }
        finally
        {
            NativeImGui.EndTable();
            NativeImGui.PopStyleVar();
        }
    }

    private void DrawLayerRow(
        GameLayerStack setting,
        IReadOnlyList<string?> ids,
        IReadOnlyList<string?> names,
        int index)
    {
        var layer = new GameLayer(index);
        NativeImGui.TableNextRow();
        _ = NativeImGui.TableSetColumnIndex(0);
        NativeImGui.AlignTextToFramePadding();
        InsetPlainCell();
        NativeImGui.TextUnformatted(index.ToString("00", CultureInfo.InvariantCulture));
        _ = NativeImGui.TableSetColumnIndex(1);
        if (layer == GameLayer.defaultLayer)
        {
            InsetPlainCell();
            NativeImGui.TextUnformatted(ids[index] ?? GameLayerId.defaultLayer.value);
            _ = NativeImGui.TableSetColumnIndex(2);
            InsetPlainCell();
            NativeImGui.TextUnformatted(names[index] ?? "Default");
            _ = NativeImGui.TableSetColumnIndex(3);
            InsetPlainCell();
            ImGuiWidget.ColoredText(EditorPalette.textDisabled, "Fixed");
            return;
        }

        NativeImGui.PushID(index);
        try
        {
            string id = m_idBuffers[index];
            bool idCommitted = DrawTextField("##layer_id", "plugin.layer-id", ref id, C_LAYER_ID_BUFFER_SIZE);
            m_idBuffers[index] = id;
            if (idCommitted)
                CommitDefinition(setting, layer, id, m_nameBuffers[index]);

            _ = NativeImGui.TableSetColumnIndex(2);
            string name = m_nameBuffers[index];
            bool nameCommitted = DrawTextField("##layer_name", "Layer name", ref name, C_LAYER_NAME_BUFFER_SIZE);
            m_nameBuffers[index] = name;
            if (nameCommitted)
                CommitDefinition(setting, layer, m_idBuffers[index], name);

            _ = NativeImGui.TableSetColumnIndex(3);
            InsetPlainCell();
            Vector2 removeSize = new(
                NativeImGui.CalcTextSize("Remove").X,
                NativeImGui.GetFrameHeight());
            if (ImGuiWidget.ClickableText(
                    $"remove_layer_{index}",
                    "Remove",
                    removeSize,
                    "Remove this project contribution while preserving Scene slot assignments."))
            {
                RemoveDefinition(setting, layer);
            }
        }
        finally
        {
            NativeImGui.PopID();
        }
    }

    private static bool DrawTextField(
        string id,
        string hint,
        ref string value,
        nuint capacity)
    {
        NativeImGui.SetNextItemWidth(-1f);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBgHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBgActive, EditorPalette.transparent);
        try
        {
            bool submitted = NativeImGui.InputTextWithHint(
                id,
                hint,
                ref value,
                capacity,
                ImGuiInputTextFlags.EnterReturnsTrue);
            return submitted || NativeImGui.IsItemDeactivatedAfterEdit();
        }
        finally
        {
            NativeImGui.PopStyleColor(3);
        }
    }

    private void DrawLayerToolbar(GameLayerStack setting, string?[] names, int definedCount)
    {
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        ImGuiWidget.LabelChip("Defined", EditorPalette.collectionRowAlternate);
        NativeImGui.SameLine(0f, 0f);
        DrawReadOnlyCount(
            $"{definedCount} / {GameLayer.C_MAX_COUNT}",
            78f * ImGuiWidget.style.zoom);
        NativeImGui.SameLine(0f, spacing);
        DrawAddLayer(setting, names, definedCount);
    }

    private static void DrawReadOnlyCount(string value, float width)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        Vector2 size = new(MathF.Max(1f, width), NativeImGui.GetFrameHeight());
        NativeImGui.Dummy(size);
        Vector2 maximum = minimum + size;
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            minimum,
            maximum,
            NativeImGui.GetColorU32(ImGuiCol.FrameBg),
            style.FrameRounding);
        drawList.AddRect(
            minimum,
            maximum,
            NativeImGui.GetColorU32(ImGuiCol.Border),
            style.FrameRounding,
            ImGuiWidget.style.borderSize);
        Vector2 textSize = NativeImGui.CalcTextSize(value);
        drawList.AddText(
            minimum + (size - textSize) * 0.5f,
            NativeImGui.GetColorU32(ImGuiCol.Text),
            value);
    }

    private static void DrawLayerTableHeader()
    {
        uint background = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionHeader);
        NativeImGui.TableNextRow();
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, background);
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, background);
        DrawHeaderCell(0, "Slot");
        DrawHeaderCell(1, "Stable ID");
        DrawHeaderCell(2, "Name");
        DrawHeaderCell(3, "Action");
    }

    private static void DrawHeaderCell(int column, string label)
    {
        _ = NativeImGui.TableSetColumnIndex(column);
        InsetPlainCell();
        NativeImGui.TextUnformatted(label);
    }

    private static void InsetPlainCell()
        => NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() + NativeImGui.GetStyle().FramePadding.X);

    private void DrawAddLayer(GameLayerStack setting, string?[] names, int definedCount)
    {
        NativeImGui.BeginDisabled(definedCount >= GameLayer.C_MAX_COUNT);
        try
        {
            float selectorWidth = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
            if (ImGuiWidget.BeginMenuSelector(
                    "add_game_layer",
                    "Add layer...",
                    selectorWidth,
                    selectorWidth))
            {
                try
                {
                    for (int index = 1; index < GameLayer.C_MAX_COUNT; index++)
                    {
                        if (!string.IsNullOrWhiteSpace(names[index]))
                            continue;
                        string slot = index.ToString("00", CultureInfo.InvariantCulture);
                        if (!NativeImGui.Selectable($"{slot}  Layer {index}"))
                            continue;
                        var layer = new GameLayer(index);
                        CommitDefinition(
                            setting,
                            layer,
                            $"project.layer.{slot}",
                            $"Layer {index}");
                    }
                }
                finally
                {
                    ImGuiWidget.EndMenuSelector();
                }
            }
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }

    private void CommitDefinition(
        GameLayerStack setting,
        GameLayer layer,
        string id,
        string name)
    {
        try
        {
            setting.Define(layer, new GameLayerId(id), name);
            SynchronizeSlot(setting, layer);
            m_error = string.Empty;
        }
        catch (ArgumentException exception)
        {
            m_error = exception.Message;
            SynchronizeSlot(setting, layer);
        }
    }

    private void RemoveDefinition(GameLayerStack setting, GameLayer layer)
    {
        _ = setting.Remove(layer);
        SynchronizeSlot(setting, layer);
        m_error = string.Empty;
    }

    private void SynchronizeBuffers(
        IReadOnlyList<string?> ids,
        IReadOnlyList<string?> names)
    {
        if (m_observedIds is not null
            && m_observedNames is not null
            && m_observedIds.SequenceEqual(ids, StringComparer.Ordinal)
            && m_observedNames.SequenceEqual(names, StringComparer.Ordinal))
        {
            return;
        }
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            m_idBuffers[index] = ids[index] ?? string.Empty;
            m_nameBuffers[index] = names[index] ?? string.Empty;
        }
        m_observedIds = ids.ToArray();
        m_observedNames = names.ToArray();
    }

    private void SynchronizeSlot(GameLayerStack setting, GameLayer layer)
    {
        m_idBuffers[layer.index] = setting.GetId(layer)?.value ?? string.Empty;
        m_nameBuffers[layer.index] = setting.GetName(layer) ?? string.Empty;
        m_observedIds = CaptureIds(setting);
        m_observedNames = CaptureNames(setting);
    }

    private static string?[] CaptureIds(GameLayerStack stack)
    {
        var result = new string?[GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            result[index] = stack.GetId(new GameLayer(index))?.value;
        return result;
    }

    private static string?[] CaptureNames(GameLayerStack stack)
    {
        var result = new string?[GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            result[index] = stack.GetName(new GameLayer(index));
        return result;
    }
}
