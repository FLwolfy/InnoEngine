using System;
using System.Numerics;
using System.Text;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    private const float C_MIN_PROPERTY_LABEL_WIDTH = 96f;
    private const float C_MAX_PROPERTY_LABEL_WIDTH = 180f;
    private const float C_PROPERTY_LABEL_RATIO = 0.40f;

    private static readonly Vector4 s_axisXColor = new(0.76f, 0.20f, 0.22f, 1f);
    private static readonly Vector4 s_axisYColor = new(0.16f, 0.62f, 0.30f, 1f);
    private static readonly Vector4 s_axisZColor = new(0.20f, 0.34f, 0.78f, 1f);
    private static readonly Vector4 s_axisWColor = new(0.48f, 0.48f, 0.50f, 1f);

    /// <summary>
    /// Draws a two-column property row with a stable internal identifier.
    /// </summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="label">Human-readable property label.</param>
    /// <param name="drawValue">Value control callback.</param>
    /// <param name="labelWidth">Optional fixed label column width.</param>
    public static void PropertyRow(
        string id,
        string label,
        Action drawValue,
        float labelWidth = -1f)
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

        float resolvedLabelWidth = labelWidth > 0f
            ? labelWidth
            : Math.Clamp(NativeImGui.GetContentRegionAvail().X * C_PROPERTY_LABEL_RATIO,
                C_MIN_PROPERTY_LABEL_WIDTH,
                C_MAX_PROPERTY_LABEL_WIDTH);
        NativeImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, resolvedLabelWidth);
        NativeImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 1f);
        NativeImGui.TableNextRow();
        NativeImGui.TableSetColumnIndex(0);
        NativeImGui.AlignTextToFramePadding();
        NativeImGui.TextUnformatted(label);
        NativeImGui.TableSetColumnIndex(1);
        NativeImGui.SetNextItemWidth(-1f);
        drawValue();
        NativeImGui.EndTable();
    }

    /// <summary>
    /// Draws a float drag field with a compact colored axis prefix.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="axis">Axis label such as X, Y, Z, or W.</param>
    /// <param name="value">Mutable numeric value.</param>
    /// <param name="width">Total width including the axis prefix.</param>
    /// <param name="speed">Drag speed.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool AxisDragFloat(string id, string axis, ref float value, float width, float speed = 0.1f)
    {
        DrawAxisPrefix(id, axis, width);
        return NativeImGui.DragFloat($"##axis_float_{id}_{axis}", ref value, speed);
    }

    /// <summary>
    /// Draws an integer drag field with a compact colored axis prefix.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="axis">Axis label such as X, Y, Z, or W.</param>
    /// <param name="value">Mutable numeric value.</param>
    /// <param name="width">Total width including the axis prefix.</param>
    /// <param name="speed">Drag speed.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool AxisDragInt(string id, string axis, ref int value, float width, float speed = 1f)
    {
        DrawAxisPrefix(id, axis, width);
        return NativeImGui.DragInt($"##axis_int_{id}_{axis}", ref value, speed);
    }

    /// <summary>
    /// Draws content inside an ImGui disabled scope when requested.
    /// </summary>
    /// <param name="disabled">Whether interaction is disabled.</param>
    /// <param name="draw">Drawing callback.</param>
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
    /// Draws a horizontal insertion marker in screen coordinates.
    /// </summary>
    /// <param name="fromX">Marker start X coordinate.</param>
    /// <param name="toX">Marker end X coordinate.</param>
    /// <param name="y">Marker Y coordinate.</param>
    public static void InsertionLine(float fromX, float toX, float y)
    {
        uint color = NativeImGui.GetColorU32(ImGuiCol.DragDropTarget);
        NativeImGui.GetWindowDrawList().AddLine(new Vector2(fromX, y), new Vector2(toX, y), color, 2f);
    }

    /// <summary>
    /// Converts an identifier into a readable editor label.
    /// </summary>
    /// <param name="name">Source identifier.</param>
    /// <returns>A label with separators and title casing.</returns>
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
        float axisWidth = MathF.Min(MathF.Max(18f, height), width * 0.36f);
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
            NativeImGui.ColorConvertFloat4ToU32(Vector4.One),
            axis);

        NativeImGui.Dummy(new Vector2(axisWidth, height));
        NativeImGui.SameLine(0f, 0f);
        NativeImGui.SetNextItemWidth(MathF.Max(24f, width - axisWidth));
    }

    private static Vector4 GetAxisColor(string axis)
    {
        return axis.ToUpperInvariant() switch
        {
            "X" or "R" => s_axisXColor,
            "Y" or "G" => s_axisYColor,
            "Z" or "B" => s_axisZColor,
            _ => s_axisWColor
        };
    }
}
