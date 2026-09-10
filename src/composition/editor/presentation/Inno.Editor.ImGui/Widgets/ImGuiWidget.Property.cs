using System;
using System.Numerics;
using System.Text;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    private const string C_COMPACT_FLOAT_FORMAT = "%.1f";
    private const string C_PRECISE_FLOAT_FORMAT = "%.9g";

    /// <summary>
    /// Draws a two-column property row with a stable internal identifier.
    /// </summary>
    /// <param name="id">
    /// Stable row identifier.
    /// </param>
    /// <param name="label">
    /// Human-readable property label.
    /// </param>
    /// <param name="drawValue">
    /// Value control callback.
    /// </param>
    /// <param name="labelWidth">
    /// Optional fixed label column width.
    /// </param>
    /// <param name="tooltip">
    /// Optional hover help displayed from the property label.
    /// </param>
    public static void PropertyRow(
        string id,
        string label,
        Action drawValue,
        float labelWidth = -1f,
        string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(drawValue);

        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp
            | ImGuiTableFlags.NoSavedSettings
            | ImGuiTableFlags.NoPadOuterX;
        if (!NativeImGui.BeginTable($"##property_{id}", 2, flags))
        {
            return;
        }

        try
        {
            float availableWidth = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
            float desiredLabelWidth = labelWidth > 0f
                ? labelWidth
                : Math.Clamp(availableWidth * style.propertyLabelRatio,
                    style.propertyLabelMinimumWidth,
                    style.propertyLabelMaximumWidth);
            float tablePadding = NativeImGui.GetStyle().CellPadding.X * 2f;
            float maximumLabelWidth = MathF.Max(
                1f,
                availableWidth - style.axisValueMinimumWidth - tablePadding);
            float resolvedLabelWidth = MathF.Min(desiredLabelWidth, maximumLabelWidth);
            NativeImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, resolvedLabelWidth);
            NativeImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 1f);
            NativeImGui.TableNextRow();
            NativeImGui.TableSetColumnIndex(0);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(label);
            DrawItemTooltip(tooltip);
            NativeImGui.TableSetColumnIndex(1);
            NativeImGui.SetNextItemWidth(-1f);
            drawValue();
        }
        finally
        {
            NativeImGui.EndTable();
        }
    }

    /// <summary>
    /// Draws a float drag field with a compact colored axis prefix.
    /// </summary>
    /// <param name="id">
    /// Stable control identifier.
    /// </param>
    /// <param name="axis">
    /// Axis label such as X, Y, Z, or W.
    /// </param>
    /// <param name="value">
    /// Mutable numeric value.
    /// </param>
    /// <param name="width">
    /// Total width including the axis prefix.
    /// </param>
    /// <param name="speed">
    /// Drag speed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changed.
    /// </returns>
    public static bool AxisDragFloat(string id, string axis, ref float value, float width, float speed = 0.1f)
    {
        DrawAxisPrefix(id, axis, width);
        return CompactDragFloat($"##axis_float_{id}_{axis}", ref value, speed);
    }

    /// <summary>
    /// Draws a floating-point drag field with a compact one-decimal presentation and precise text editing.
    /// </summary>
    /// <param name="label">
    /// Stable ImGui label and identifier.
    /// </param>
    /// <param name="value">
    /// Exact floating-point value to edit. Compact presentation never rounds the stored value.
    /// </param>
    /// <param name="speed">
    /// Drag speed.
    /// </param>
    /// <param name="minimum">
    /// Optional inclusive lower bound.
    /// </param>
    /// <param name="maximum">
    /// Optional inclusive upper bound.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the exact value changed.
    /// </returns>
    public static bool CompactDragFloat(
        string label,
        ref float value,
        float speed = 0.1f,
        float? minimum = null,
        float? maximum = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ValidateFloatBounds(minimum, maximum);
        bool bounded = minimum.HasValue;
        ImGuiSliderFlags flags = ImGuiSliderFlags.NoRoundToFormat |
                                 (bounded ? ImGuiSliderFlags.AlwaysClamp : ImGuiSliderFlags.None);
        string format = WantsPreciseFloatInput(label)
            ? C_PRECISE_FLOAT_FORMAT
            : C_COMPACT_FLOAT_FORMAT;
        return NativeImGui.DragFloat(
            label,
            ref value,
            speed,
            minimum ?? 0f,
            maximum ?? 0f,
            format,
            flags);
    }

    /// <summary>
    /// Draws a bounded slider with compact presentation and a double-click precise input mode.
    /// </summary>
    /// <param name="label">
    /// Stable ImGui label and identifier.
    /// </param>
    /// <param name="value">
    /// Exact floating-point value to edit. Compact presentation never rounds the stored value.
    /// </param>
    /// <param name="minimum">
    /// Inclusive lower bound.
    /// </param>
    /// <param name="maximum">
    /// Inclusive upper bound.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the exact value changed.
    /// </returns>
    public static bool CompactSliderFloat(
        string label,
        ref float value,
        float minimum,
        float maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ValidateFloatBounds(minimum, maximum);
        ImGuiSliderFlags flags = ImGuiSliderFlags.NoRoundToFormat | ImGuiSliderFlags.AlwaysClamp;
        if (WantsPreciseFloatInput(label))
        {
            float speed = MathF.Max((maximum - minimum) * 0.01f, 0.0001f);
            return NativeImGui.DragFloat(
                label,
                ref value,
                speed,
                minimum,
                maximum,
                C_PRECISE_FLOAT_FORMAT,
                flags);
        }
        return NativeImGui.SliderFloat(
            label,
            ref value,
            minimum,
            maximum,
            C_COMPACT_FLOAT_FORMAT,
            flags);
    }

    /// <summary>
    /// Draws an integer drag field with a compact colored axis prefix.
    /// </summary>
    /// <param name="id">
    /// Stable control identifier.
    /// </param>
    /// <param name="axis">
    /// Axis label such as X, Y, Z, or W.
    /// </param>
    /// <param name="value">
    /// Mutable numeric value.
    /// </param>
    /// <param name="width">
    /// Total width including the axis prefix.
    /// </param>
    /// <param name="speed">
    /// Drag speed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value changed.
    /// </returns>
    public static bool AxisDragInt(string id, string axis, ref int value, float width, float speed = 1f)
    {
        DrawAxisPrefix(id, axis, width);
        return NativeImGui.DragInt($"##axis_int_{id}_{axis}", ref value, speed);
    }

    /// <summary>
    /// Draws content inside an ImGui disabled scope when requested.
    /// </summary>
    /// <param name="disabled">
    /// Whether interaction is disabled.
    /// </param>
    /// <param name="draw">
    /// Drawing callback.
    /// </param>
    public static void Disabled(bool disabled, Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        NativeImGui.BeginDisabled(disabled);
        try
        {
            draw();
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }

    /// <summary>
    /// Draws a horizontal insertion marker above normal window content in screen coordinates.
    /// </summary>
    /// <param name="fromX">
    /// Marker start X coordinate.
    /// </param>
    /// <param name="toX">
    /// Marker end X coordinate.
    /// </param>
    /// <param name="y">
    /// Marker Y coordinate.
    /// </param>
    public static void InsertionLine(float fromX, float toX, float y)
    {
        uint color = NativeImGui.GetColorU32(ImGuiCol.DragDropTarget);
        NativeImGui.GetForegroundDrawList().AddLine(
            new Vector2(fromX, y),
            new Vector2(toX, y),
            color,
            style.interactionOverlayThickness);
    }

    /// <summary>
    /// Draws the standard yellow rectangular drag-and-drop target highlight above all normal
    /// window content in screen coordinates.
    /// </summary>
    /// <param name="min">
    /// Minimum target coordinate.
    /// </param>
    /// <param name="max">
    /// Maximum target coordinate.
    /// </param>
    public static void DropTargetHighlight(Vector2 min, Vector2 max)
    {
        NativeImGui.GetForegroundDrawList().AddRect(
            min,
            max,
            NativeImGui.GetColorU32(ImGuiCol.DragDropTarget),
            style.frameRounding,
            ImDrawFlags.None,
            style.interactionOverlayThickness);
    }

    /// <summary>
    /// Converts an identifier into a readable editor label.
    /// </summary>
    /// <param name="name">
    /// Source identifier.
    /// </param>
    /// <returns>
    /// A label with separators and title casing.
    /// </returns>
    public static string NicifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length + 8);
        char previous = '\0';
        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];
            if (current == '_' || current == '-')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                {
                    builder.Append(' ');
                }

                previous = current;
                continue;
            }

            if (i > 0 && char.IsUpper(current) && char.IsLower(previous) && builder[builder.Length - 1] != ' ')
            {
                builder.Append(' ');
            }

            builder.Append(builder.Length == 0 ? char.ToUpperInvariant(current) : current);
            previous = current;
        }

        return builder.ToString();
    }

    private static void DrawAxisPrefix(string id, string axis, float width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);

        float height = NativeImGui.GetFrameHeight();
        float axisWidth = MathF.Min(
            MathF.Max(style.axisPrefixMinimumWidth, height),
            width * style.axisPrefixWidthRatio);
        Vector2 prefixMin = NativeImGui.GetCursorScreenPos();
        Vector2 prefixMax = prefixMin + new Vector2(axisWidth, height);
        NativeImGui.GetWindowDrawList().AddRectFilled(
            prefixMin,
            prefixMax,
            NativeImGui.ColorConvertFloat4ToU32(GetAxisColor(axis)),
            1f);

        Vector2 textSize = NativeImGui.CalcTextSize(axis);
        Vector2 textPosition = prefixMin + (new Vector2(axisWidth, height) - textSize) * 0.5f;
        NativeImGui.GetWindowDrawList().AddText(
            textPosition,
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.text),
            axis);

        NativeImGui.Dummy(new Vector2(axisWidth, height));
        NativeImGui.SameLine(0f, 0f);
        NativeImGui.SetNextItemWidth(MathF.Max(1f, width - axisWidth));
    }

    private static bool WantsPreciseFloatInput(string label)
    {
        bool inputRequested = NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) ||
                              (NativeImGui.GetIO().KeyCtrl &&
                               NativeImGui.IsMouseClicked(ImGuiMouseButton.Left));
        if (!inputRequested)
            return false;
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        Vector2 maximum = minimum + new Vector2(
            MathF.Max(1f, NativeImGui.CalcItemWidth()),
            NativeImGui.GetFrameHeight());
        return NativeImGui.IsMouseHoveringRect(minimum, maximum, true);
    }

    private static void ValidateFloatBounds(float? minimum, float? maximum)
    {
        if (minimum.HasValue != maximum.HasValue)
        {
            throw new ArgumentException("Floating-point bounds must either both be supplied or both be omitted.");
        }
        if (minimum is not float lower || maximum is not float upper)
            return;
        if (!float.IsFinite(lower) || !float.IsFinite(upper) || lower > upper)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Floating-point bounds must be finite and ordered.");
    }

    private static Vector4 GetAxisColor(string axis)
    {
        return axis.ToUpperInvariant() switch
        {
            "X" or "R" => EditorPalette.axisX,
            "Y" or "G" => EditorPalette.axisY,
            "Z" or "B" => EditorPalette.axisZ,
            _ => EditorPalette.axisW
        };
    }
}
