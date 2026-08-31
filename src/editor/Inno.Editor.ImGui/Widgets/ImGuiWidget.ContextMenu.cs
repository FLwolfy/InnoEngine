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
        NativeImGui.EndPopup();
        PopContextMenuStyle();
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
