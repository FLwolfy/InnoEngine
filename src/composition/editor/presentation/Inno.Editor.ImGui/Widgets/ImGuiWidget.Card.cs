using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a component-style collapsible card header.
    /// </summary>
    /// <param name="id">
    /// The stable card identifier.
    /// </param>
    /// <param name="title">
    /// The visible card title.
    /// </param>
    /// <param name="drawLeadingControl">
    /// An optional control drawn before the title.
    /// </param>
    /// <param name="drawTrailingControl">
    /// An optional control aligned to the right edge.
    /// </param>
    /// <param name="defaultOpen">
    /// Whether the card starts expanded.
    /// </param>
    /// <param name="dimmed">
    /// Whether header controls and title use the inactive text color.
    /// </param>
    /// <param name="trailingControlWidth">
    /// The optional width reserved for the trailing control group.
    /// </param>
    /// <param name="drawContextMenu">
    /// An optional callback that binds a context menu to the complete header item.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when card content should be drawn; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool CollapsingCard(
        string id,
        string title,
        Action? drawLeadingControl = null,
        Action? drawTrailingControl = null,
        bool defaultOpen = true,
        bool dimmed = false,
        float trailingControlWidth = 0f,
        Action? drawContextMenu = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        if (trailingControlWidth < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trailingControlWidth),
                trailingControlWidth,
                "Trailing control width cannot be negative.");
        }
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.AllowOverlap
            | ImGuiTreeNodeFlags.OpenOnArrow;
        if (defaultOpen)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.inspectorCardHeader);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.inspectorCardHeader);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.inspectorCardHeader);
        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.transparent);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inspectorCardHeaderPadding);
        Vector2 itemHeaderMin;
        Vector2 headerMin;
        Vector2 headerMax;
        Vector2 contentCursor;
        bool open;
        try
        {
            Vector2 headerCursor = NativeImGui.GetCursorScreenPos();
            (float cardLeft, float cardRight) = GetFullWidthCardBounds(headerCursor);
            Vector2 persistentHeaderMin = new(cardLeft, headerCursor.Y);
            Vector2 persistentHeaderMax = new(cardRight, headerCursor.Y + NativeImGui.GetFrameHeight());
            NativeImGui.GetWindowDrawList().AddRectFilled(
                persistentHeaderMin,
                persistentHeaderMax,
                NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorCardHeader),
                1f);
            open = NativeImGui.TreeNodeEx($"##card_{id}", flags);
            itemHeaderMin = NativeImGui.GetItemRectMin();
            headerMin = new Vector2(cardLeft, persistentHeaderMin.Y);
            headerMax = new Vector2(cardRight, persistentHeaderMax.Y);
            contentCursor = NativeImGui.GetCursorScreenPos();
        }
        finally
        {
            NativeImGui.PopStyleVar();
            NativeImGui.PopStyleColor(4);
        }
        try
        {
            drawContextMenu?.Invoke();

            float contentX = itemHeaderMin.X + NativeImGui.GetTreeNodeToLabelSpacing();
            DrawDisclosureIndicator(
                new Vector2(itemHeaderMin.X, headerMin.Y),
                new Vector2(contentX, headerMax.Y),
                open,
                dimmed);
            float contentY = headerMin.Y + MathF.Max(
                0f,
                (headerMax.Y - headerMin.Y - NativeImGui.GetFrameHeight()) * 0.5f);
            float trailingWidth = drawTrailingControl is null
                ? 0f
                : trailingControlWidth > 0f
                    ? trailingControlWidth
                    : NativeImGui.GetFrameHeight();
            float titleRight = headerMax.X - trailingWidth;
            if (drawTrailingControl is not null)
                titleRight -= style.inspectorHeaderControlSpacing;
            NativeImGui.SetCursorScreenPos(new Vector2(contentX, contentY));
            bool dimmedColorPushed = false;
            bool groupStarted = false;
            try
            {
                if (dimmed)
                {
                    NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.inspectorCardDisabledText);
                    dimmedColorPushed = true;
                }
                NativeImGui.BeginGroup();
                groupStarted = true;
                if (drawLeadingControl is not null)
                {
                    drawLeadingControl();
                    NativeImGui.SameLine(0f, style.inspectorHeaderControlSpacing);
                }
                NativeImGui.AlignTextToFramePadding();
                Vector2 titleCursor = NativeImGui.GetCursorScreenPos();
                float titleWidth = MathF.Max(1f, titleRight - titleCursor.X);
                ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
                drawList.PushClipRect(
                    titleCursor,
                    new Vector2(titleCursor.X + titleWidth, headerMax.Y),
                    true);
                try
                {
                    drawList.AddText(titleCursor, NativeImGui.GetColorU32(ImGuiCol.Text), title);
                }
                finally
                {
                    drawList.PopClipRect();
                }
                NativeImGui.Dummy(new Vector2(titleWidth, NativeImGui.GetFrameHeight()));
                NativeImGui.EndGroup();
                groupStarted = false;

                if (drawTrailingControl is not null)
                {
                    NativeImGui.SetCursorScreenPos(new Vector2(
                        MathF.Max(contentX, headerMax.X - trailingWidth),
                        contentY));
                    drawTrailingControl();
                }
            }
            finally
            {
                if (groupStarted)
                    NativeImGui.EndGroup();
                if (dimmedColorPushed)
                    NativeImGui.PopStyleColor();
                NativeImGui.SetCursorScreenPos(contentCursor);
            }
            return open;
        }
        catch
        {
            if (open)
                NativeImGui.TreePop();
            throw;
        }
    }

    /// <summary>
    /// Draws a vertically centered disclosure triangle with button-style hover feedback.
    /// </summary>
    /// <param name="min">
    /// The minimum screen coordinate of the indicator area.
    /// </param>
    /// <param name="max">
    /// The maximum screen coordinate of the indicator area.
    /// </param>
    /// <param name="open">
    /// Whether the represented content is expanded.
    /// </param>
    /// <param name="dimmed">
    /// Whether the indicator uses the inactive text color.
    /// </param>
    public static void DrawDisclosureIndicator(
        Vector2 min,
        Vector2 max,
        bool open,
        bool dimmed = false)
    {
        Vector2 availableSize = Vector2.Max(Vector2.Zero, max - min);
        float buttonSize = MathF.Max(
            1f,
            MathF.Min(availableSize.X, availableSize.Y) - style.disclosureButtonInset * 2f);
        Vector2 buttonMin = min + (availableSize - new Vector2(buttonSize)) * 0.5f;
        Vector2 buttonMax = buttonMin + new Vector2(buttonSize);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        if (!IsPopupBlockingInteraction() &&
            NativeImGui.IsMouseHoveringRect(buttonMin, buttonMax))
        {
            drawList.AddRectFilled(
                buttonMin,
                buttonMax,
                NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorCardDisclosureHovered),
                NativeImGui.GetStyle().FrameRounding);
        }

        string indicator = open ? "▼" : "▶";
        Vector2 indicatorSize = NativeImGui.CalcTextSize(indicator);
        Vector2 indicatorPosition = buttonMin + (new Vector2(buttonSize) - indicatorSize) * 0.5f;
        uint color = NativeImGui.ColorConvertFloat4ToU32(
            dimmed
                ? EditorPalette.inspectorCardDisabledText
                : EditorPalette.text);
        drawList.AddText(indicatorPosition, color, indicator);
    }

    /// <summary>
    /// Draws expanded collapsible-card content inside a framed body.
    /// </summary>
    /// <param name="id">
    /// The stable card identifier.
    /// </param>
    /// <param name="drawContent">
    /// The callback that draws card content.
    /// </param>
    /// <param name="dimmed">
    /// Whether content is visually disabled and non-interactive.
    /// </param>
    public static void CardBody(string id, Action drawContent, bool dimmed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(drawContent);

        Vector2 originalCursor = NativeImGui.GetCursorScreenPos();
        (float cardLeft, float cardRight) = GetFullWidthCardBounds(originalCursor);
        NativeImGui.SetCursorScreenPos(new Vector2(cardLeft, originalCursor.Y));
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, EditorPalette.inspectorCardBody);
        NativeImGui.PushStyleColor(ImGuiCol.Border, EditorPalette.inspectorCardBodyBorder);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, style.borderSize);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inspectorCardBodyPadding);
        ImGuiChildFlags childFlags = ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY;
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar |
                                       ImGuiWindowFlags.NoScrollWithMouse |
                                       ImGuiWindowFlags.NoSavedSettings;
        bool visible = NativeImGui.BeginChild(
            $"##card_body_{id}",
            new Vector2(MathF.Max(1f, cardRight - cardLeft), 0f),
            childFlags,
            windowFlags);
        bool disabled = false;
        try
        {
            if (visible && dimmed)
            {
                NativeImGui.BeginDisabled(true);
                disabled = true;
            }
            if (visible)
                drawContent();
        }
        finally
        {
            if (disabled)
                NativeImGui.EndDisabled();
            NativeImGui.EndChild();
            float nextY = NativeImGui.GetCursorScreenPos().Y;
            NativeImGui.PopStyleVar(3);
            NativeImGui.PopStyleColor(2);
            NativeImGui.SetCursorScreenPos(new Vector2(originalCursor.X, nextY));
        }
    }

    private static (float left, float right) GetFullWidthCardBounds(Vector2 cursor)
    {
        float horizontalPadding = ImGuiP.GetCurrentWindow().WindowPadding.X;
        float left = cursor.X - horizontalPadding;
        float right = cursor.X + NativeImGui.GetContentRegionAvail().X + horizontalPadding;
        return (left, MathF.Max(left + 1f, right));
    }
}
