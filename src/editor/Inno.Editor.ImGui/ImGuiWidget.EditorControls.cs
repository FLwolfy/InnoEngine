using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Describes the outcome of an inline rename control.
/// </summary>
public enum InlineRenameResult
{
    /// <summary>
    /// Editing is still in progress.
    /// </summary>
    None,

    /// <summary>
    /// The edited text should be committed.
    /// </summary>
    Commit,

    /// <summary>
    /// The edited text should be discarded.
    /// </summary>
    Cancel
}

public static partial class ImGuiWidget
{
    private static readonly Vector4 s_cardHeaderColor = new(0.42f, 0.39f, 0.51f, 1f);
    private static readonly Vector4 s_cardBodyColor = new(0.12f, 0.12f, 0.14f, 1f);
    private static readonly Vector4 s_cardBodyBorderColor = new(0.28f, 0.27f, 0.32f, 1f);
    private static readonly Vector4 s_cardDisabledTextColor = new(0.52f, 0.52f, 0.54f, 1f);
    private static readonly Vector4 s_iconHoveredColor = new(0.76f, 0.69f, 0.94f, 1f);

    /// <summary>
    /// Draws a single-line search field with a stable identifier.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="hint">Empty-value hint text.</param>
    /// <param name="query">Mutable search query.</param>
    /// <param name="capacity">Maximum UTF-8 buffer capacity.</param>
    /// <param name="width">Control width, or a negative value to fill available space.</param>
    /// <returns><see langword="true"/> when the query changed.</returns>
    public static bool SearchInput(
        string id,
        string hint,
        ref string query,
        nuint capacity = 256,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        NativeImGui.SetNextItemWidth(width);
        return NativeImGui.InputTextWithHint($"##search_{id}", hint, ref query, capacity);
    }

    /// <summary>
    /// Begins a popup and draws its focused search field.
    /// </summary>
    /// <param name="id">Popup identifier previously passed to ImGui OpenPopup.</param>
    /// <param name="query">Mutable search query.</param>
    /// <param name="hint">Empty-value hint text.</param>
    /// <param name="capacity">Maximum UTF-8 buffer capacity.</param>
    /// <param name="width">Search field width.</param>
    /// <returns><see langword="true"/> when popup content should be drawn.</returns>
    public static bool BeginSearchPopup(
        string id,
        ref string query,
        string hint,
        nuint capacity = 256,
        float width = 280f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!NativeImGui.BeginPopup(id))
        {
            return false;
        }

        if (NativeImGui.IsWindowAppearing())
        {
            NativeImGui.SetKeyboardFocusHere();
        }

        _ = SearchInput(id, hint, ref query, capacity, width);
        NativeImGui.Separator();
        return true;
    }

    /// <summary>
    /// Ends a popup opened by <see cref="BeginSearchPopup"/>.
    /// </summary>
    public static void EndSearchPopup() => NativeImGui.EndPopup();

    /// <summary>
    /// Begins a right-click context menu for the most recently submitted item.
    /// </summary>
    /// <param name="id">Stable popup identifier.</param>
    /// <returns><see langword="true"/> when context menu content should be drawn.</returns>
    public static bool BeginContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return NativeImGui.BeginPopupContextItem(id, ImGuiPopupFlags.MouseButtonRight);
    }

    /// <summary>
    /// Ends a context menu opened by <see cref="BeginContextMenu"/>.
    /// </summary>
    public static void EndContextMenu() => NativeImGui.EndPopup();

    /// <summary>
    /// Draws an inline rename field and reports commit or cancellation gestures.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="text">Mutable text buffer.</param>
    /// <param name="requestFocus">Whether keyboard focus should be requested this frame.</param>
    /// <param name="capacity">Maximum UTF-8 buffer capacity.</param>
    /// <param name="width">Control width, or a negative value to fill available space.</param>
    /// <returns>The current rename outcome.</returns>
    public static InlineRenameResult InlineRename(
        string id,
        ref string text,
        ref bool requestFocus,
        nuint capacity = 512,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (requestFocus)
        {
            NativeImGui.SetKeyboardFocusHere();
            requestFocus = false;
        }

        NativeImGui.SetNextItemWidth(width);
        bool submitted = NativeImGui.InputText(
            $"##rename_{id}",
            ref text,
            capacity,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (NativeImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            return InlineRenameResult.Cancel;
        }

        return submitted || NativeImGui.IsItemDeactivated()
            ? InlineRenameResult.Commit
            : InlineRenameResult.None;
    }

    /// <summary>
    /// Draws a compact icon-only button without a persistent background.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="icon">Visible icon glyph.</param>
    /// <param name="tooltip">Optional hover tooltip.</param>
    /// <returns><see langword="true"/> when the button is pressed.</returns>
    public static bool IconButton(string id, string icon, string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(icon);

        Vector2 iconSize = NativeImGui.CalcTextSize(icon);
        Vector2 controlSize = GetIconButtonSize();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        bool pressed = NativeImGui.InvisibleButton($"##icon_button_{id}", controlSize);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();

        uint color = hovered || active
            ? NativeImGui.ColorConvertFloat4ToU32(s_iconHoveredColor)
            : NativeImGui.GetColorU32(ImGuiCol.Text);
        NativeImGui.GetWindowDrawList().AddText(cursor + (controlSize - iconSize) * 0.5f, color, icon);

        if (!string.IsNullOrWhiteSpace(tooltip) && hovered && NativeImGui.BeginTooltip())
        {
            NativeImGui.TextUnformatted(tooltip);
            NativeImGui.EndTooltip();
        }

        return pressed;
    }

    /// <summary>
    /// Gets the fixed control size used by <see cref="IconButton"/>.
    /// </summary>
    /// <returns>A size with the same fixed-width icon slot convention as IconText.</returns>
    public static Vector2 GetIconButtonSize()
    {
        float iconSlotWidth = NativeImGui.GetTextLineHeight();
        return new Vector2(iconSlotWidth + 6f, NativeImGui.GetFrameHeight());
    }

    /// <summary>
    /// Draws a compact checkbox whose checked fill uses the current text color.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="value">Mutable checked state.</param>
    /// <param name="size">Visual square size in pixels.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool CompactCheckbox(string id, ref bool value, float size = 13f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (size <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Checkbox size must be positive.");
        }

        Vector2 initialCursor = NativeImGui.GetCursorScreenPos();
        float frameHeight = NativeImGui.GetFrameHeight();
        float verticalOffset = MathF.Max(0f, (frameHeight - size) * 0.5f);
        Vector2 min = initialCursor + new Vector2(0f, verticalOffset);
        bool changed = NativeImGui.InvisibleButton(
            $"##compact_checkbox_{id}",
            new Vector2(size, frameHeight));
        if (changed)
        {
            value = !value;
        }

        bool hovered = NativeImGui.IsItemHovered();
        Vector2 max = min + new Vector2(size);
        uint textColor = NativeImGui.GetColorU32(ImGuiCol.Text);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        if (value)
        {
            drawList.AddRectFilled(min, max, textColor, 1f);
            uint checkColor = NativeImGui.ColorConvertFloat4ToU32(s_cardHeaderColor);
            drawList.AddLine(
                min + new Vector2(size * 0.22f, size * 0.52f),
                min + new Vector2(size * 0.43f, size * 0.72f),
                checkColor,
                1.5f);
            drawList.AddLine(
                min + new Vector2(size * 0.43f, size * 0.72f),
                min + new Vector2(size * 0.80f, size * 0.28f),
                checkColor,
                1.5f);
        }
        else
        {
            drawList.AddRect(min, max, textColor, 1f);
        }

        if (hovered)
        {
            drawList.AddRect(
                min - Vector2.One,
                max + Vector2.One,
                NativeImGui.ColorConvertFloat4ToU32(s_iconHoveredColor),
                1f,
                1.5f);
        }

        return changed;
    }

    /// <summary>
    /// Draws a horizontally centered button with optional space above it.
    /// </summary>
    /// <param name="label">Visible label and optional ImGui identifier suffix.</param>
    /// <param name="topPadding">Additional vertical space above the button.</param>
    /// <returns><see langword="true"/> when the button is pressed.</returns>
    public static bool CenteredButton(string label, float topPadding = 0f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (topPadding < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(topPadding), topPadding, "Top padding cannot be negative.");
        }

        if (topPadding > 0f)
        {
            NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + topPadding);
        }

        float width = NativeImGui.CalcTextSize(label).X + NativeImGui.GetStyle().FramePadding.X * 2f;
        float offset = MathF.Max(0f, (NativeImGui.GetContentRegionAvail().X - width) * 0.5f);
        NativeImGui.SetCursorPosX(NativeImGui.GetCursorPosX() + offset);
        return NativeImGui.Button(label);
    }

    /// <summary>
    /// Draws a component-style collapsible card header.
    /// </summary>
    /// <param name="id">Stable card identifier.</param>
    /// <param name="title">Visible card title.</param>
    /// <param name="drawLeadingControl">Optional control drawn before the title.</param>
    /// <param name="drawTrailingControl">Optional control aligned to the right edge.</param>
    /// <param name="defaultOpen">Whether the card starts expanded.</param>
    /// <param name="dimmed">Whether header controls and title use the inactive text color.</param>
    /// <returns><see langword="true"/> when card content should be drawn.</returns>
    public static bool CollapsingCard(
        string id,
        string title,
        Action? drawLeadingControl = null,
        Action? drawTrailingControl = null,
        bool defaultOpen = true,
        bool dimmed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.AllowOverlap
            | ImGuiTreeNodeFlags.OpenOnArrow;
        if (defaultOpen)
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        NativeImGui.PushStyleColor(ImGuiCol.Header, s_cardHeaderColor);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, s_cardHeaderColor);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, s_cardHeaderColor);
        NativeImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 1f));
        Vector2 headerCursor = NativeImGui.GetCursorScreenPos();
        (float cardLeft, float cardRight) = GetFullWidthCardBounds(headerCursor);
        Vector2 persistentHeaderMin = new(cardLeft, headerCursor.Y);
        Vector2 persistentHeaderMax = new(cardRight, headerCursor.Y + NativeImGui.GetFrameHeight());
        NativeImGui.GetWindowDrawList().AddRectFilled(
            persistentHeaderMin,
            persistentHeaderMax,
            NativeImGui.ColorConvertFloat4ToU32(s_cardHeaderColor),
            1f);
        bool open = NativeImGui.TreeNodeEx($"##card_{id}", flags);
        Vector2 itemHeaderMin = NativeImGui.GetItemRectMin();
        Vector2 itemHeaderMax = NativeImGui.GetItemRectMax();
        Vector2 headerMin = new(cardLeft, itemHeaderMin.Y);
        Vector2 headerMax = new(cardRight, itemHeaderMax.Y);
        Vector2 contentCursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.PopStyleVar();
        NativeImGui.PopStyleColor(4);

        float contentX = itemHeaderMin.X + NativeImGui.GetTreeNodeToLabelSpacing();
        DrawDisclosureIndicator(
            new Vector2(itemHeaderMin.X, headerMin.Y),
            new Vector2(contentX, headerMax.Y),
            open,
            dimmed);
        float contentY = headerMin.Y + MathF.Max(0f, (headerMax.Y - headerMin.Y - NativeImGui.GetFrameHeight()) * 0.5f);
        NativeImGui.SetCursorScreenPos(new Vector2(contentX, contentY));
        if (dimmed)
            NativeImGui.PushStyleColor(ImGuiCol.Text, s_cardDisabledTextColor);
        NativeImGui.BeginGroup();
        if (drawLeadingControl is not null)
        {
            drawLeadingControl();
            NativeImGui.SameLine(0f, 4f);
        }

        NativeImGui.AlignTextToFramePadding();
        NativeImGui.TextUnformatted(title);
        NativeImGui.EndGroup();

        if (drawTrailingControl is not null)
        {
            float trailingWidth = NativeImGui.GetFrameHeight();
            NativeImGui.SetCursorScreenPos(new Vector2(
                MathF.Max(contentX, headerMax.X - trailingWidth),
                contentY));
            drawTrailingControl();
        }
        if (dimmed)
            NativeImGui.PopStyleColor();

        NativeImGui.SetCursorScreenPos(contentCursor);
        return open;
    }

    /// <summary>
    /// Draws a vertically centered disclosure triangle with button-style hover feedback.
    /// </summary>
    /// <param name="min">Minimum screen coordinate of the indicator area.</param>
    /// <param name="max">Maximum screen coordinate of the indicator area.</param>
    /// <param name="open">Whether the represented content is expanded.</param>
    /// <param name="dimmed">Whether to draw the indicator with inactive text color.</param>
    public static void DrawDisclosureIndicator(
        Vector2 min,
        Vector2 max,
        bool open,
        bool dimmed = false)
    {
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        if (NativeImGui.IsMouseHoveringRect(min, max))
        {
            Vector4 hovered = NativeImGui.ColorConvertU32ToFloat4(
                NativeImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.82f));
            drawList.AddRectFilled(
                min,
                max,
                NativeImGui.ColorConvertFloat4ToU32(hovered),
                NativeImGui.GetStyle().FrameRounding);
        }

        string indicator = open ? "▼" : "▶";
        Vector2 indicatorSize = NativeImGui.CalcTextSize(indicator);
        Vector2 indicatorPosition = new(
            min.X + MathF.Max(0f, (max.X - min.X - indicatorSize.X) * 0.5f),
            min.Y + MathF.Max(0f, (max.Y - min.Y - indicatorSize.Y) * 0.5f));
        uint color = NativeImGui.ColorConvertFloat4ToU32(
            dimmed
                ? s_cardDisabledTextColor
                : NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.Text)));
        drawList.AddText(indicatorPosition, color, indicator);
    }

    /// <summary>
    /// Draws the expanded content of a collapsible card inside a framed body.
    /// </summary>
    /// <param name="id">Stable card identifier.</param>
    /// <param name="drawContent">Callback that draws the card content.</param>
    /// <param name="dimmed">Whether the content is visually disabled and non-interactive.</param>
    public static void CardBody(string id, Action drawContent, bool dimmed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(drawContent);

        Vector2 originalCursor = NativeImGui.GetCursorScreenPos();
        (float cardLeft, float cardRight) = GetFullWidthCardBounds(originalCursor);
        NativeImGui.SetCursorScreenPos(new Vector2(cardLeft, originalCursor.Y));
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, s_cardBodyColor);
        NativeImGui.PushStyleColor(ImGuiCol.Border, s_cardBodyBorderColor);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 5f));
        ImGuiChildFlags childFlags = ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY;
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar |
                                       ImGuiWindowFlags.NoScrollWithMouse |
                                       ImGuiWindowFlags.NoSavedSettings;
        if (NativeImGui.BeginChild(
                $"##card_body_{id}",
                new Vector2(MathF.Max(1f, cardRight - cardLeft), 0f),
                childFlags,
                windowFlags))
        {
            if (dimmed)
                NativeImGui.BeginDisabled(true);
            drawContent();
            if (dimmed)
                NativeImGui.EndDisabled();
        }
        NativeImGui.EndChild();
        float nextY = NativeImGui.GetCursorScreenPos().Y;
        NativeImGui.PopStyleVar(3);
        NativeImGui.PopStyleColor(2);
        NativeImGui.SetCursorScreenPos(new Vector2(originalCursor.X, nextY));
    }

    private static (float left, float right) GetFullWidthCardBounds(Vector2 cursor)
    {
        float horizontalPadding = NativeImGui.GetStyle().WindowPadding.X;
        float left = cursor.X - horizontalPadding;
        float right = cursor.X + NativeImGui.GetContentRegionAvail().X + horizontalPadding;
        return (left, MathF.Max(left + 1f, right));
    }
}
