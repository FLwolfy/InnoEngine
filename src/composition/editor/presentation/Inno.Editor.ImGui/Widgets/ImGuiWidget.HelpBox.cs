using System;
using System.Numerics;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides standard help box presentation in the Editor UI.
/// </summary>
public static partial class ImGuiWidget
{
    [ThreadStatic]
    private static SectionLayoutState? s_sectionLayout;

    /// <summary>
    /// Draws a wrapped contextual message with a semantic icon and a subdued status surface.
    /// </summary>
    /// <param name="text">
    /// Literal message shown within the current content width.
    /// </param>
    /// <param name="icon">
    /// Icon-font glyph identifying the message severity.
    /// </param>
    /// <param name="color">
    /// Semantic accent for the icon, outline, and leading stripe.
    /// </param>
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
        background.W = EditorPalette.opacityOpaque;
        Vector4 outline = color;
        outline.W = EditorPalette.opacityMuted;
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
    /// Draws a square collection-section band whose background, separator, and padding are shared
    /// with hierarchy and Inspector presentation.
    /// </summary>
    /// <param name="title">
    /// Literal section title.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="title"/> is <see langword="null"/>.
    /// </exception>
    public static void CollectionSectionHeader(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float height = NativeImGui.GetTextLineHeight() + style.sectionHeaderPadding.Y * 2f;
        NativeImGui.GetWindowDrawList().AddRectFilled(
            origin,
            origin + new Vector2(width, height),
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.hierarchySceneRow));
        NativeImGui.PushStyleColor(ImGuiCol.Separator, EditorPalette.inspectorSectionBorder);
        try
        {
            NativeImGui.SeparatorText(title);
        }
        finally
        {
            NativeImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Draws content in a scope where consecutive section headers become framed fieldsets.
    /// </summary>
    /// <param name="drawContent">
    /// Callback that draws all content participating in the section layout.
    /// </param>
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

    /// <summary>
    /// Gets whether content belonging to the current Inspector section should be drawn.
    /// </summary>
    /// <remarks>
    /// Attribute-driven drawers use this value to skip properties until the next section header
    /// when the current section is collapsed.
    /// </remarks>
    public static bool isSectionContentVisible => s_sectionLayout?.contentVisible ?? true;

    /// <summary>
    /// Ensures that subsequently drawn default properties belong to a framed section.
    /// </summary>
    /// <param name="title">
    /// Title used only when the current section layout has not declared a section yet.
    /// </param>
    /// <param name="description">
    /// Optional hover explanation for the implicit section title.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the current section accepts content; otherwise,
    /// <see langword="false"/> when that section is collapsed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="title"/> is <see langword="null"/>.
    /// </exception>
    public static bool EnsureSection(
        string title = "Properties",
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        SectionLayoutState? layout = s_sectionLayout;
        if (layout is null)
            return true;
        return layout.sectionIndex == 0
            ? SectionHeader(title, description)
            : layout.contentVisible;
    }

    /// <summary>
    /// Draws a section heading with hover-only description using the shared Inspector presentation.
    /// </summary>
    /// <param name="title">
    /// Section title.
    /// </param>
    /// <param name="description">
    /// Optional hover explanation.
    /// </param>
    /// <param name="drawLeadingControl">
    /// Optional interactive control drawn before the section title.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="title"/> is <see langword="null"/>.
    /// </exception>
    /// <returns>
    /// <see langword="true"/> when the section content should be drawn; otherwise,
    /// <see langword="false"/> when the section is collapsed.
    /// </returns>
    public static bool SectionHeader(
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
            return true;
        }

        CompleteSection(layout);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float frameHeight = NativeImGui.GetFrameHeight();
        float paddingX = style.inspectorSectionPadding.X;
        float legendInset = style.sectionHeaderPadding.X;
        int sectionIndex = layout.sectionIndex++;
        uint stateId = NativeImGui.GetID($"##inspector_section_{sectionIndex}");
        ImGuiStoragePtr storage = NativeImGui.GetStateStorage();
        bool open = NativeImGui.GetBool(storage, stateId, true);

        NativeImGui.SetCursorScreenPos(new Vector2(origin.X + legendInset, origin.Y));
        bool titlePressed = false;
        Vector2 titleSize = GetClickableTextSize(title, Vector2.Zero);
        NativeImGui.BeginGroup();
        try
        {
            if (drawLeadingControl is not null)
            {
                drawLeadingControl();
                NativeImGui.SameLine(0f, style.inspectorHeaderControlSpacing);
            }

            titlePressed = ClickableText(
                $"inspector_section_title_{sectionIndex}",
                title,
                new Vector2(MathF.Max(1f, titleSize.X), frameHeight),
                description);
        }
        finally
        {
            NativeImGui.EndGroup();
        }

        if (titlePressed)
        {
            open = !open;
            NativeImGui.SetBool(storage, stateId, open);
        }

        Vector2 legendMin = NativeImGui.GetItemRectMin();
        Vector2 legendMax = NativeImGui.GetItemRectMax();
        Vector2 contentCursor = NativeImGui.GetCursorScreenPos();
        contentCursor.X = origin.X;
        float top = origin.Y + frameHeight * 0.5f;
        float legendLeft = legendMin.X - style.inspectorSectionLegendGap;
        float legendRight = legendMax.X + style.inspectorSectionLegendGap;

        NativeImGui.SetCursorScreenPos(contentCursor);
        layout.contentVisible = open;
        if (!open)
        {
            DrawCollapsedSectionBorder(
                NativeImGui.GetWindowDrawList(),
                origin.X,
                origin.X + width,
                top,
                legendLeft,
                legendRight);
            NativeImGui.Dummy(new Vector2(0f, style.inspectorSectionSpacing));
            return false;
        }

        NativeImGui.Dummy(new Vector2(0f, style.inspectorSectionPadding.Y));
        NativeImGui.Indent(paddingX);
        layout.active = true;
        layout.left = origin.X;
        layout.right = origin.X + width;
        layout.top = top;
        layout.legendLeft = legendLeft;
        layout.legendRight = legendRight;
        return true;
    }

    private static void DrawCollapsedSectionBorder(
        ImDrawListPtr draw,
        float left,
        float right,
        float top,
        float legendLeft,
        float legendRight)
    {
        uint color = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorSectionBorder);
        DrawCappedSectionSegment(
            draw,
            left,
            MathF.Max(left, legendLeft),
            top,
            capAtStart: true,
            color);
        DrawCappedSectionSegment(
            draw,
            MathF.Min(right, legendRight),
            right,
            top,
            capAtStart: false,
            color);
    }

    private static void DrawCappedSectionSegment(
        ImDrawListPtr draw,
        float from,
        float to,
        float centerY,
        bool capAtStart,
        uint color)
    {
        float thickness = style.borderSize;
        float halfThickness = thickness * 0.5f;
        float halfCap = style.inspectorCollapsedSectionCapLength * 0.5f;
        float capX = capAtStart ? from : to;
        float innerX = capAtStart
            ? MathF.Min(to, capX + halfThickness)
            : MathF.Max(from, capX - halfThickness);
        draw.AddLine(
            new Vector2(capX, centerY - halfCap),
            new Vector2(capX, centerY + halfCap),
            color,
            thickness);
        draw.AddLine(
            new Vector2(innerX, centerY),
            new Vector2(capAtStart ? to : from, centerY),
            color,
            thickness);
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
        ImDrawListPtr draw = NativeImGui.GetWindowDrawList();
        float radius = MathF.Min(
            style.inspectorSectionRounding,
            MathF.Max(0f, MathF.Min(layout.right - layout.left, bottom - layout.top) * 0.5f));
        float leftLegend = MathF.Max(layout.left + radius, layout.legendLeft);
        float rightLegend = MathF.Min(layout.right - radius, layout.legendRight);
        draw.PathLineTo(new Vector2(leftLegend, layout.top));
        draw.PathLineTo(new Vector2(layout.left + radius, layout.top));
        draw.PathBezierQuadraticCurveTo(
            new Vector2(layout.left, layout.top),
            new Vector2(layout.left, layout.top + radius));
        draw.PathLineTo(new Vector2(layout.left, bottom - radius));
        draw.PathBezierQuadraticCurveTo(
            new Vector2(layout.left, bottom),
            new Vector2(layout.left + radius, bottom));
        draw.PathLineTo(new Vector2(layout.right - radius, bottom));
        draw.PathBezierQuadraticCurveTo(
            new Vector2(layout.right, bottom),
            new Vector2(layout.right, bottom - radius));
        draw.PathLineTo(new Vector2(layout.right, layout.top + radius));
        draw.PathBezierQuadraticCurveTo(
            new Vector2(layout.right, layout.top),
            new Vector2(layout.right - radius, layout.top));
        draw.PathLineTo(new Vector2(rightLegend, layout.top));
        draw.PathStroke(color, ImDrawFlags.None, style.borderSize);
        NativeImGui.Dummy(new Vector2(0f, style.inspectorSectionSpacing));
        layout.active = false;
        layout.contentVisible = true;
    }

    private sealed class SectionLayoutState
    {
        internal bool active;
        internal bool contentVisible = true;
        internal int sectionIndex;
        internal float left;
        internal float right;
        internal float top;
        internal float legendLeft;
        internal float legendRight;
    }
}
