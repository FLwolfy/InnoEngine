using System;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Begins an explicitly opened popup using the editor context-menu presentation contract.
    /// The popup sizes itself to its submitted content and never creates an implicit scroll range.
    /// </summary>
    /// <param name="id">The stable identifier previously passed to ImGui when opening the popup.</param>
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
        ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize |
                                 ImGuiWindowFlags.NoScrollbar |
                                 ImGuiWindowFlags.NoScrollWithMouse |
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
    /// Begins a tooltip using the same padding, colors, border, and spacing as editor menus.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when tooltip content should be submitted; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public static bool BeginMenuTooltip()
    {
        PushContextMenuStyle();
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
    /// Begins a styled right-click context menu for the most recently submitted item.
    /// </summary>
    /// <param name="id">The stable popup identifier in the current ImGui ID scope.</param>
    /// <returns><see langword="true"/> when context-menu content should be drawn; otherwise, <see langword="false"/>.</returns>
    public static bool BeginContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PushContextMenuStyle();
        if (NativeImGui.BeginPopupContextItem(id, ImGuiPopupFlags.MouseButtonRight))
            return true;
        PopContextMenuStyle();
        return false;
    }

    /// <summary>
    /// Begins a styled right-click context menu for the current window's unoccupied background.
    /// </summary>
    /// <param name="id">The stable popup identifier in the current ImGui ID scope.</param>
    /// <returns><see langword="true"/> when context-menu content should be drawn; otherwise, <see langword="false"/>.</returns>
    public static bool BeginWindowContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PushContextMenuStyle();
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
