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
    private const nuint C_LAYER_NAME_BUFFER_SIZE = 128;

    private readonly string[] m_nameBuffers = new string[GameLayer.C_MAX_COUNT];
    private string m_error = string.Empty;
    private string?[]? m_observedNames;

    /// <summary>
    /// Gets or sets the setting id exposed by this implementation.
    /// </summary>
    public override ProjectSettingId settingId => GameLayerStack.settingId;

    /// <summary>
    /// Gets the section exposed by this implementation.
    /// </summary>
    public override string section => "Definitions";

    /// <summary>
    /// Gets the description exposed by this implementation.
    /// </summary>
    public override string description
        => "Define layer names and slots. Stable IDs are generated as projectId.name and are never authored manually.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The setting supplied to this operation.
    /// </param>
    protected override void OnDraw(GameLayerStack setting)
    {
        string?[] names = CaptureNames(setting);
        SynchronizeBuffers(names);
        DrawLayerRows(setting, names);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    private void DrawLayerRows(GameLayerStack setting, string?[] names)
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
        if (!NativeImGui.BeginTable("##game_layer_definitions", 3, flags))
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
                "Name",
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize);
            NativeImGui.TableSetupColumn(
                "Action",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                72f * ImGuiWidget.style.zoom);
            DrawLayerTableHeader();
            for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                    DrawLayerRow(setting, names, index);
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
            NativeImGui.TextUnformatted(names[index] ?? "Default");
            _ = NativeImGui.TableSetColumnIndex(2);
            InsetPlainCell();
            ImGuiWidget.ColoredText(EditorPalette.textDisabled, "Fixed");
            return;
        }

        NativeImGui.PushID(index);
        try
        {
            string name = m_nameBuffers[index];
            bool committed = DrawTextField("##layer_name", "Layer name", ref name);
            m_nameBuffers[index] = name;
            if (committed)
                CommitDefinition(setting, layer, name);

            _ = NativeImGui.TableSetColumnIndex(2);
            InsetPlainCell();
            Vector2 removeSize = new(
                NativeImGui.CalcTextSize("Remove").X,
                NativeImGui.GetFrameHeight());
            if (ImGuiWidget.ClickableText(
                    $"remove_layer_{index}",
                    "Remove",
                    removeSize,
                    "Remove this definition while preserving Scene slot assignments."))
            {
                _ = setting.Remove(layer);
                SynchronizeSlot(setting, layer);
                m_error = string.Empty;
            }
        }
        finally
        {
            NativeImGui.PopID();
        }
    }

    private static bool DrawTextField(string id, string hint, ref string value)
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
                C_LAYER_NAME_BUFFER_SIZE,
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
        DrawReadOnlyCount($"{definedCount} / {GameLayer.C_MAX_COUNT}", 78f * ImGuiWidget.style.zoom);
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
        drawList.AddRectFilled(minimum, maximum, NativeImGui.GetColorU32(ImGuiCol.FrameBg), style.FrameRounding);
        drawList.AddRect(
            minimum,
            maximum,
            NativeImGui.GetColorU32(ImGuiCol.Border),
            style.FrameRounding,
            ImGuiWidget.style.borderSize);
        Vector2 textSize = NativeImGui.CalcTextSize(value);
        drawList.AddText(minimum + (size - textSize) * 0.5f, NativeImGui.GetColorU32(ImGuiCol.Text), value);
    }

    private static void DrawLayerTableHeader()
    {
        uint background = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionHeader);
        NativeImGui.TableNextRow();
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, background);
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, background);
        DrawHeaderCell(0, "Slot");
        DrawHeaderCell(1, "Name");
        DrawHeaderCell(2, "Action");
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
            if (!ImGuiWidget.BeginMenuSelector(
                    "add_game_layer",
                    "Add layer...",
                    selectorWidth,
                    selectorWidth))
            {
                return;
            }
            try
            {
                for (int index = 1; index < GameLayer.C_MAX_COUNT; index++)
                {
                    if (!string.IsNullOrWhiteSpace(names[index]))
                        continue;
                    string slot = index.ToString("00", CultureInfo.InvariantCulture);
                    if (NativeImGui.Selectable($"{slot}  Layer {index}"))
                        CommitDefinition(setting, new GameLayer(index), $"Layer {index}");
                }
            }
            finally
            {
                ImGuiWidget.EndMenuSelector();
            }
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }

    private void CommitDefinition(GameLayerStack setting, GameLayer layer, string name)
    {
        try
        {
            setting.Define(layer, name);
            SynchronizeSlot(setting, layer);
            m_error = string.Empty;
        }
        catch (ArgumentException exception)
        {
            m_error = exception.Message;
            SynchronizeSlot(setting, layer);
        }
    }

    private void SynchronizeBuffers(IReadOnlyList<string?> names)
    {
        if (m_observedNames is not null
            && m_observedNames.SequenceEqual(names, StringComparer.Ordinal))
        {
            return;
        }
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            m_nameBuffers[index] = names[index] ?? string.Empty;
        m_observedNames = names.ToArray();
    }

    private void SynchronizeSlot(GameLayerStack setting, GameLayer layer)
    {
        m_nameBuffers[layer.index] = setting.GetName(layer) ?? string.Empty;
        m_observedNames = CaptureNames(setting);
    }

    private static string?[] CaptureNames(GameLayerStack stack)
    {
        var result = new string?[GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            result[index] = stack.GetName(new GameLayer(index));
        return result;
    }
}
