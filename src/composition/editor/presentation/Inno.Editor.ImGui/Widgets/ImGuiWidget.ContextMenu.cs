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
    private const float C_TOOLTIP_MINIMUM_WIDTH = 300f;
    private const float C_TOOLTIP_WRAP_WIDTH = 440f;

    /// <summary>
    /// Begins an explicitly opened popup using the editor context-menu presentation contract.
    /// The popup stays in its parent viewport, sizes itself to submitted content, and scrolls when
    /// its work-area bound is reached.
    /// </summary>
    /// <param name="id">
    /// The stable identifier previously passed to ImGui when opening the popup.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when popup content should be submitted; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    public static bool BeginMenuPopup(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PushContextMenuStyle();
        NativeImGui.SetNextWindowViewport(NativeImGui.GetWindowViewport().ID);
        ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoSavedSettings;
        if (NativeImGui.BeginPopup(id, flags))
            return true;
        PopContextMenuStyle();
        return false;
    }

    /// <summary>
    /// Ends a popup opened by <see cref="BeginMenuPopup"/> and restores the previous style.
    /// </summary>
    public static void EndMenuPopup()
    {
        NativeImGui.EndPopup();
        PopContextMenuStyle();
    }

    /// <summary>
    /// Begins a parent-viewport tooltip using the same padding, colors, border, and spacing as editor menus.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when tooltip content should be submitted; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static bool BeginMenuTooltip()
    {
        PushContextMenuStyle();
        NativeImGui.SetNextWindowViewport(NativeImGui.GetWindowViewport().ID);
        if (NativeImGui.BeginTooltip())
            return true;
        PopContextMenuStyle();
        return false;
    }

    /// <summary>
    /// Ends a tooltip opened by <see cref="BeginMenuTooltip"/> and restores the previous style.
    /// </summary>
    public static void EndMenuTooltip()
    {
        NativeImGui.EndTooltip();
        PopContextMenuStyle();
    }

    /// <summary>
    /// Draws a consistently sized, wrapped editor tooltip for the most recently submitted item.
    /// </summary>
    /// <param name="text">
    /// Tooltip text. Empty values do not draw a tooltip.
    /// </param>
    public static void DrawItemTooltip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !NativeImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            return;
        }

        DrawTooltip(text);
    }

    /// <summary>Draws the standard viewport-clamped tooltip when a custom-drawn canvas element is hovered.</summary>
    /// <param name="text">Tooltip contents. The caller owns hit testing; empty text draws nothing.</param>
    public static void DrawTooltip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        ImGuiViewportPtr viewport = NativeImGui.GetWindowViewport();
        Vector2 margin = new(6f * style.zoom);
        Vector2 available = Vector2.Max(Vector2.One, viewport.WorkSize - margin * 2f);
        Vector2 padding = style.menuWindowPadding;
        float outerWidth = MathF.Min(C_TOOLTIP_WRAP_WIDTH * style.zoom, available.X);
        float innerWidth = MathF.Max(1f, outerWidth - padding.X * 2f - style.menuBorderSize * 2f);
        Vector2 textSize = NativeImGui.CalcTextSize(text, false, innerWidth);
        outerWidth = MathF.Min(outerWidth, MathF.Max(
            MathF.Min(C_TOOLTIP_MINIMUM_WIDTH * style.zoom, available.X),
            textSize.X + padding.X * 2f + style.menuBorderSize * 2f));
        innerWidth = MathF.Max(1f, outerWidth - padding.X * 2f - style.menuBorderSize * 2f);
        textSize = NativeImGui.CalcTextSize(text, false, innerWidth);
        Vector2 size = new(outerWidth, MathF.Min(available.Y, textSize.Y + padding.Y * 2f + style.menuBorderSize * 2f));
        Vector2 workMin = viewport.WorkPos + margin;
        Vector2 workMax = workMin + available;
        Vector2 mouse = NativeImGui.GetMousePos();
        Vector2 position = mouse + new Vector2(16f, 20f) * style.zoom;
        if (position.X + size.X > workMax.X)
            position.X = mouse.X - size.X - 12f * style.zoom;
        if (position.Y + size.Y > workMax.Y)
            position.Y = mouse.Y - size.Y - 12f * style.zoom;
        position = Vector2.Clamp(position, workMin, Vector2.Max(workMin, workMax - size));
        NativeImGui.SetNextWindowPos(position);
        NativeImGui.SetNextWindowSize(size);
        if (!BeginMenuTooltip())
            return;
        try
        {
            NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + MathF.Max(1f, NativeImGui.GetContentRegionAvail().X));
            try
            {
                NativeImGui.TextUnformatted(text);
            }
            finally
            {
                NativeImGui.PopTextWrapPos();
            }
        }
        finally
        {
            EndMenuTooltip();
        }
    }

    /// <summary>
    /// Begins a parent-viewport styled right-click context menu for the most recently submitted item.
    /// </summary>
    /// <param name="id">
    /// The stable popup identifier in the current ImGui ID scope.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when context-menu content should be drawn; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool BeginContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PushContextMenuStyle();
        NativeImGui.SetNextWindowViewport(NativeImGui.GetWindowViewport().ID);
        if (NativeImGui.BeginPopupContextItem(id, ImGuiPopupFlags.MouseButtonRight))
            return true;
        PopContextMenuStyle();
        return false;
    }

    /// <summary>
    /// Begins a parent-viewport styled right-click context menu for the current window's unoccupied background.
    /// </summary>
    /// <param name="id">
    /// The stable popup identifier in the current ImGui ID scope.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when context-menu content should be drawn; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool BeginWindowContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PushContextMenuStyle();
        NativeImGui.SetNextWindowViewport(NativeImGui.GetWindowViewport().ID);
        if (NativeImGui.BeginPopupContextWindow(
                id,
                ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            return true;
        }
        PopContextMenuStyle();
        return false;
    }

    /// <summary>
    /// Ends a context menu opened by <see cref="BeginContextMenu"/> or <see cref="BeginWindowContextMenu"/>.
    /// </summary>
    public static void EndContextMenu()
    {
        EndMenuPopup();
    }

    private static bool IsPopupBlockingInteraction()
        => NativeImGui.IsPopupOpen(
            string.Empty,
            ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

    private static void PushContextMenuStyle()
    {
        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.menuText);
        NativeImGui.PushStyleColor(ImGuiCol.PopupBg, EditorPalette.menuBackground);
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.menuItem);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.menuItemHovered);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.menuItemActive);
        NativeImGui.PushStyleColor(ImGuiCol.Separator, EditorPalette.menuSeparator);
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, style.menuWindowPadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.menuFramePadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.menuItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, style.menuRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, style.menuBorderSize);
    }

    private static void PopContextMenuStyle()
    {
        NativeImGui.PopStyleVar(5);
        NativeImGui.PopStyleColor(6);
    }
}
