using System;
using System.Numerics;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

public static partial class ImGuiWidget
{
    [ThreadStatic]
    private static SectionLayoutState? s_sectionLayout;

    /// <summary>Draws a wrapped contextual message with a semantic icon and a subdued status surface.</summary>
    /// <param name="text">Literal message shown within the current content width.</param>
    /// <param name="icon">Icon-font glyph identifying the message severity.</param>
    /// <param name="color">Semantic accent for the icon, outline, and leading stripe.</param>
    public static void HelpBox(string text, string icon, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(icon);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 padding = new(10f * style.zoom, 8f * style.zoom);
        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float iconWidth = NativeImGui.CalcTextSize(icon).X;
        float gap = 8f * style.zoom;
        float textWidth = MathF.Max(1f, width - padding.X * 2f - iconWidth - gap);
        Vector2 textSize = NativeImGui.CalcTextSize(text, false, textWidth);
        float height = MathF.Max(NativeImGui.GetTextLineHeight(), textSize.Y) + padding.Y * 2f;
        ImDrawListPtr draw = NativeImGui.GetWindowDrawList();
        Vector4 background = Vector4.Lerp(EditorPalette.windowBackground, color, 0.075f);
        background.W = 1f;
        Vector4 outline = color;
        outline.W = 0.28f;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), NativeImGui.ColorConvertFloat4ToU32(background), 4f * style.zoom);
        draw.AddRect(origin, origin + new Vector2(width, height), NativeImGui.ColorConvertFloat4ToU32(outline), 4f * style.zoom);
        draw.AddRectFilled(origin, origin + new Vector2(2f * style.zoom, height), NativeImGui.ColorConvertFloat4ToU32(color));
        draw.AddText(origin + padding, NativeImGui.ColorConvertFloat4ToU32(color), icon);
        NativeImGui.SetCursorScreenPos(origin + padding + new Vector2(iconWidth + gap, 0f));
        NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + textWidth);
        try { NativeImGui.TextUnformatted(text); }
        finally { NativeImGui.PopTextWrapPos(); }
        NativeImGui.SetCursorScreenPos(origin);
        NativeImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// Draws content in a scope where consecutive section headers become framed fieldsets.
    /// </summary>
    /// <param name="drawContent">Callback that draws all content participating in the section layout.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="drawContent"/> is <see langword="null"/>.
    /// </exception>
    public static void SectionLayout(Action drawContent)
    {
        ArgumentNullException.ThrowIfNull(drawContent);
        SectionLayoutState? parent = s_sectionLayout;
        var current = new SectionLayoutState();
        s_sectionLayout = current;
        try
        {
            drawContent();
        }
        finally
        {
            CompleteSection(current);
            s_sectionLayout = parent;
        }
    }

    /// <summary>Draws a section heading with hover-only description using the shared Inspector presentation.</summary>
    /// <param name="title">Section title.</param>
    /// <param name="description">Optional hover explanation.</param>
    /// <param name="drawLeadingControl">Optional interactive control drawn before the section title.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="title"/> is <see langword="null"/>.
    /// </exception>
    public static void SectionHeader(
        string title,
        string? description = null,
        Action? drawLeadingControl = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        SectionLayoutState? layout = s_sectionLayout;
        if (layout is null)
        {
            NativeImGui.SeparatorText(title);
            DrawItemTooltip(description);
            return;
        }

        CompleteSection(layout);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float frameHeight = NativeImGui.GetFrameHeight();
        float paddingX = style.inspectorSectionPadding.X;
        NativeImGui.SetCursorScreenPos(origin + new Vector2(paddingX, 0f));
        NativeImGui.BeginGroup();
        try
        {
            if (drawLeadingControl is not null)
            {
                drawLeadingControl();
                NativeImGui.SameLine(0f, style.inspectorHeaderControlSpacing);
            }
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(title);
            DrawItemTooltip(description);
        }
        finally
        {
            NativeImGui.EndGroup();
        }

        Vector2 legendMin = NativeImGui.GetItemRectMin();
        Vector2 legendMax = NativeImGui.GetItemRectMax();
        NativeImGui.SetCursorScreenPos(origin);
        NativeImGui.Dummy(new Vector2(width, frameHeight + style.inspectorSectionPadding.Y));
        NativeImGui.Indent(paddingX);
        layout.active = true;
        layout.left = origin.X;
        layout.right = origin.X + width;
        layout.top = origin.Y + frameHeight * 0.5f;
        layout.legendLeft = legendMin.X - style.inspectorSectionLegendGap;
        layout.legendRight = legendMax.X + style.inspectorSectionLegendGap;
    }

    private static void CompleteSection(SectionLayoutState layout)
    {
        if (!layout.active)
            return;

        NativeImGui.Dummy(new Vector2(0f, style.inspectorSectionPadding.Y));
        NativeImGui.Unindent(style.inspectorSectionPadding.X);
        float bottom = MathF.Max(
            NativeImGui.GetCursorScreenPos().Y,
            layout.top + NativeImGui.GetFrameHeight());
        uint color = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorSectionBorder);
        float thickness = style.borderSize;
        ImDrawListPtr draw = NativeImGui.GetWindowDrawList();
        draw.AddLine(
            new Vector2(layout.left, layout.top),
            new Vector2(MathF.Max(layout.left, layout.legendLeft), layout.top),
            color,
            thickness);
        draw.AddLine(
            new Vector2(MathF.Min(layout.right, layout.legendRight), layout.top),
            new Vector2(layout.right, layout.top),
            color,
            thickness);
        draw.AddLine(new Vector2(layout.left, layout.top), new Vector2(layout.left, bottom), color, thickness);
        draw.AddLine(new Vector2(layout.right, layout.top), new Vector2(layout.right, bottom), color, thickness);
        draw.AddLine(new Vector2(layout.left, bottom), new Vector2(layout.right, bottom), color, thickness);
        NativeImGui.Dummy(new Vector2(0f, style.inspectorSectionSpacing));
        layout.active = false;
    }

    private sealed class SectionLayoutState
    {
        internal bool active;
        internal float left;
        internal float right;
        internal float top;
        internal float legendLeft;
        internal float legendRight;
    }
}
