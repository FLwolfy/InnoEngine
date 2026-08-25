using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Engine.Scene.Layers;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[EditorSettingPath("Project/Layers/Game Layers")]
internal sealed class GameLayersSetting : EditorSetting
{
    private const nuint C_LAYER_NAME_BUFFER_SIZE = 128;

    private readonly string[] m_nameBuffers = new string[GameLayer.C_MAX_COUNT];
    private string m_error = string.Empty;
    private string?[]? m_observedNames;

    /// <inheritdoc />
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <inheritdoc />
    public override string section => "Layer Definitions";

    /// <inheritdoc />
    public override string description
        => "Assign stable names to the thirty-two layer slots used throughout the project.";

    /// <inheritdoc />
    protected override void OnDraw(EditorSettingObject setting)
    {
        string?[] names = setting.GetAsStringArray("names");
        uint[] masks = setting.GetAsUInt32Array("interactionMasks");
        _ = CreateStack(names, masks);
        SynchronizeBuffers(names);
        DrawLayerRows(setting, names, masks);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    internal static GameLayerStack CreateStack(EditorSettingObject setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return CreateStack(
            setting.GetAsStringArray("names"),
            setting.GetAsUInt32Array("interactionMasks"));
    }

    private static EditorSettingObject CreateDefault()
    {
        var stack = new GameLayerStack();
        var result = new EditorSettingObject();
        Write(result, stack);
        return result;
    }

    private static GameLayerStack CreateStack(string?[] names, uint[] masks)
    {
        if (names.Length != GameLayer.C_MAX_COUNT || masks.Length != GameLayer.C_MAX_COUNT)
            throw new InvalidOperationException("Game Layers must contain exactly thirty-two names and masks.");
        if (!string.Equals(names[0], "Default", StringComparison.Ordinal))
            throw new InvalidOperationException("Layer slot zero must be named Default.");

        var result = new GameLayerStack();
        for (int index = 1; index < GameLayer.C_MAX_COUNT; index++)
        {
            if (names[index] is { } name)
                result.Define(new GameLayer(index), name);
        }
        for (int first = 0; first < GameLayer.C_MAX_COUNT; first++)
        {
            for (int second = first; second < GameLayer.C_MAX_COUNT; second++)
            {
                bool forward = (masks[first] & (1u << second)) != 0u;
                bool reverse = (masks[second] & (1u << first)) != 0u;
                if (forward != reverse)
                {
                    throw new InvalidOperationException(
                        $"Layer interaction between slots {first} and {second} is not symmetric.");
                }
                result.SetInteraction(new GameLayer(first), new GameLayer(second), forward);
            }
        }
        return result;
    }

    private static void Write(EditorSettingObject setting, GameLayerStack stack)
    {
        var names = new string?[GameLayer.C_MAX_COUNT];
        var masks = new uint[GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            var layer = new GameLayer(index);
            names[index] = stack.GetName(layer);
            masks[index] = stack.GetInteractionMask(layer).value;
        }
        setting.SetAsStringArray("names", names);
        setting.SetAsUInt32Array("interactionMasks", masks);
    }

    private void DrawLayerRows(
        EditorSettingObject setting,
        string?[] names,
        uint[] masks)
    {
        int definedCount = names.Count(static name => !string.IsNullOrWhiteSpace(name));
        DrawLayerToolbar(setting, names, masks, definedCount);
        NativeImGui.Spacing();

        ImGuiTableFlags flags = ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.BordersInnerH |
                                ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoSavedSettings;
        if (!NativeImGui.BeginTable("##game_layer_definitions", 3, flags))
            return;
        NativeImGui.TableSetupColumn(
            "Slot",
            ImGuiTableColumnFlags.WidthFixed,
            48f * ImGuiWidget.style.zoom);
        NativeImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        NativeImGui.TableSetupColumn(
            "Action",
            ImGuiTableColumnFlags.WidthFixed,
            72f * ImGuiWidget.style.zoom);
        DrawLayerTableHeader();
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
        {
            if (string.IsNullOrWhiteSpace(names[index]))
                continue;
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
                continue;
            }

            NativeImGui.PushID(index);
            NativeImGui.SetNextItemWidth(-1f);
            string value = m_nameBuffers[index];
            NativeImGui.PushStyleColor(ImGuiCol.FrameBg, EditorPalette.transparent);
            NativeImGui.PushStyleColor(ImGuiCol.FrameBgHovered, EditorPalette.transparent);
            NativeImGui.PushStyleColor(ImGuiCol.FrameBgActive, EditorPalette.transparent);
            bool submitted = NativeImGui.InputTextWithHint(
                "##layer_name",
                "Layer name",
                ref value,
                C_LAYER_NAME_BUFFER_SIZE,
                ImGuiInputTextFlags.EnterReturnsTrue);
            NativeImGui.PopStyleColor(3);
            m_nameBuffers[index] = value;
            if (submitted || NativeImGui.IsItemDeactivatedAfterEdit())
                CommitLayerName(setting, names, masks, layer, value);
            _ = NativeImGui.TableSetColumnIndex(2);
            InsetPlainCell();
            Vector2 removeSize = new(
                NativeImGui.CalcTextSize("Remove").X,
                NativeImGui.GetFrameHeight());
            if (ImGuiWidget.ClickableText(
                    $"remove_layer_{index}",
                    "Remove",
                    removeSize,
                    "Remove this layer definition."))
            {
                CommitLayerName(setting, names, masks, layer, string.Empty);
            }
            NativeImGui.PopID();
        }
        NativeImGui.EndTable();
    }

    private void DrawLayerToolbar(
        EditorSettingObject setting,
        string?[] names,
        uint[] masks,
        int definedCount)
    {
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        ImGuiWidget.LabelChip("Defined", EditorPalette.collectionRowAlternate);
        NativeImGui.SameLine(0f, 0f);
        DrawReadOnlyCount(
            $"{definedCount} / {GameLayer.C_MAX_COUNT}",
            78f * ImGuiWidget.style.zoom);
        NativeImGui.SameLine(0f, spacing);
        DrawAddLayer(setting, names, masks, definedCount);
    }

    private static void DrawReadOnlyCount(string value, float width)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        Vector2 size = new(
            MathF.Max(1f, width),
            NativeImGui.GetFrameHeight());
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

    private void DrawAddLayer(
        EditorSettingObject setting,
        string?[] names,
        uint[] masks,
        int definedCount)
    {
        NativeImGui.BeginDisabled(definedCount >= GameLayer.C_MAX_COUNT);
        NativeImGui.SetNextItemWidth(-1f);
        if (NativeImGui.BeginCombo("##add_game_layer", "Add layer..."))
        {
            for (int index = 1; index < GameLayer.C_MAX_COUNT; index++)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                    continue;
                string slot = index.ToString("00", CultureInfo.InvariantCulture);
                if (NativeImGui.Selectable($"{slot}  Layer {index}"))
                {
                    CommitLayerName(
                        setting,
                        names,
                        masks,
                        new GameLayer(index),
                        $"Layer {index}");
                }
            }
            NativeImGui.EndCombo();
        }
        NativeImGui.EndDisabled();
    }

    private void CommitLayerName(
        EditorSettingObject setting,
        string?[] names,
        uint[] masks,
        GameLayer layer,
        string value)
    {
        try
        {
            GameLayerStack updated = CreateStack(names, masks);
            if (string.IsNullOrWhiteSpace(value))
                _ = updated.Remove(layer);
            else
                updated.Define(layer, value);
            Write(setting, updated);
            m_observedNames = CaptureNames(updated);
            m_nameBuffers[layer.index] = updated.GetName(layer) ?? string.Empty;
            m_error = string.Empty;
        }
        catch (ArgumentException exception)
        {
            m_error = exception.Message;
            m_nameBuffers[layer.index] = names[layer.index] ?? string.Empty;
        }
    }

    private void SynchronizeBuffers(string?[] names)
    {
        if (m_observedNames is not null && m_observedNames.SequenceEqual(names, StringComparer.Ordinal))
            return;
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            m_nameBuffers[index] = names[index] ?? string.Empty;
        m_observedNames = (string?[])names.Clone();
    }

    private static string?[] CaptureNames(GameLayerStack stack)
    {
        var result = new string?[GameLayer.C_MAX_COUNT];
        for (int index = 0; index < GameLayer.C_MAX_COUNT; index++)
            result[index] = stack.GetName(new GameLayer(index));
        return result;
    }
}
