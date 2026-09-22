using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Editor.Interactions;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Adapter.Presentation.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>
/// Renders immutable editor menu models through ImGui.
/// </summary>
public static class EditorMenuRenderer
{
    private static readonly Dictionary<uint, string> m_contextSearches = [];

    private readonly record struct MenuSection(
        EditorInteraction interaction,
        IReadOnlyList<EditorMenuItem> items);

    /// <summary>
    /// Draws a resolved right-click menu for the most recently submitted ImGui item.
    /// </summary>
    /// <param name="id">
    /// The stable popup identifier in the current ImGui ID scope.
    /// </param>
    /// <param name="interaction">
    /// The interaction area and optional operation target.
    /// </param>
    /// <returns>
    /// <see langword="true"/> while the context popup is open and its items were drawn.
    /// </returns>
    public static bool ContextMenu(string id, EditorInteraction interaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        uint popupId = NativeImGui.GetID(id);
        if (!ShouldResolveItemContextMenu(id))
        {
            m_contextSearches.Remove(popupId);
            return false;
        }
        EditorMenuModel menu = interaction.BuildMenu();
        if (menu.items.Count == 0)
            return false;
        if (!EditorWidget.BeginContextMenu(id))
            return false;
        try
        {
            DrawContextMenuContents(popupId, [new MenuSection(interaction, menu.items)]);
        }
        finally
        {
            EditorWidget.EndContextMenu();
        }
        return true;
    }

    /// <summary>
    /// Draws one context popup composed from an item interaction and its containing-scope interaction.
    /// </summary>
    /// <param name="id">
    /// Stable popup identifier in the current ImGui ID scope.
    /// </param>
    /// <param name="scopeInteraction">
    /// Container interaction whose commands are shown first, such as creation commands for a directory.
    /// </param>
    /// <param name="itemInteraction">
    /// Selected-item interaction whose commands are shown after the container commands.
    /// </param>
    /// <returns>
    /// <see langword="true"/> while the context popup is open and at least one command group was drawn.
    /// </returns>
    public static bool ContextMenu(
        string id,
        EditorInteraction scopeInteraction,
        EditorInteraction itemInteraction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        uint popupId = NativeImGui.GetID(id);
        if (!ShouldResolveItemContextMenu(id))
        {
            m_contextSearches.Remove(popupId);
            return false;
        }
        EditorMenuModel scopeMenu = scopeInteraction.BuildMenu();
        EditorMenuModel itemMenu = itemInteraction.BuildMenu();
        if (scopeMenu.items.Count == 0 && itemMenu.items.Count == 0)
            return false;
        if (!EditorWidget.BeginContextMenu(id))
            return false;
        try
        {
            DrawContextMenuContents(
                popupId,
                [
                    new MenuSection(scopeInteraction, scopeMenu.items),
                    new MenuSection(itemInteraction, itemMenu.items)
                ]);
        }
        finally
        {
            EditorWidget.EndContextMenu();
        }
        return true;
    }

    /// <summary>
    /// Draws a resolved right-click menu when the current ImGui window's unoccupied background is clicked.
    /// </summary>
    /// <param name="id">
    /// The stable popup identifier in the current ImGui ID scope.
    /// </param>
    /// <param name="interaction">
    /// The interaction area and directory target for the background operation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> while the background context popup is open and its items were drawn.
    /// </returns>
    public static bool WindowContextMenu(string id, EditorInteraction interaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        uint popupId = NativeImGui.GetID(id);
        if (!ShouldResolveWindowContextMenu(id))
        {
            m_contextSearches.Remove(popupId);
            return false;
        }
        EditorMenuModel menu = interaction.BuildMenu();
        if (menu.items.Count == 0)
            return false;
        if (!EditorWidget.BeginWindowContextMenu(id))
            return false;
        try
        {
            DrawContextMenuContents(popupId, [new MenuSection(interaction, menu.items)]);
        }
        finally
        {
            EditorWidget.EndContextMenu();
        }
        return true;
    }

    private static void DrawContextMenuContents(
        uint popupId,
        IReadOnlyList<MenuSection> sections)
    {
        string search = NativeImGui.IsWindowAppearing()
            ? string.Empty
            : m_contextSearches.GetValueOrDefault(popupId, string.Empty);
        NativeImGui.PushStyleVar(
            ImGuiStyleVar.FramePadding,
            EditorWidget.style.menuSearchFramePadding);
        NativeImGui.PushStyleColor(ImGuiCol.NavCursor, Vector4.Zero);
        try
        {
            if (NativeImGui.IsWindowAppearing())
                NativeImGui.SetKeyboardFocusHere();
            _ = EditorWidget.SearchInput(
                $"context-menu-{popupId}",
                "Search commands…",
                ref search,
                width: EditorWidget.style.searchPopupWidth);
        }
        finally
        {
            NativeImGui.PopStyleColor();
            NativeImGui.PopStyleVar();
        }
        m_contextSearches[popupId] = search;
        NativeImGui.Separator();

        bool searching = !string.IsNullOrWhiteSpace(search);
        bool drewSection = false;
        foreach (MenuSection section in sections)
        {
            if (section.items.Count == 0)
                continue;
            if (!searching && drewSection)
                NativeImGui.Separator();
            if (searching)
            {
                if (!DrawSearchItems(section.interaction, section.items, search))
                    continue;
                NativeImGui.CloseCurrentPopup();
                return;
            }
            DrawItems(section.interaction, section.items);
            drewSection = true;
        }
    }

    private static bool ShouldResolveItemContextMenu(string id)
        => NativeImGui.IsPopupOpen(id) ||
           NativeImGui.IsItemHovered() && NativeImGui.IsMouseReleased(Inno.Native.ImGui.ImGuiMouseButton.Right);

    private static bool ShouldResolveWindowContextMenu(string id)
        => NativeImGui.IsPopupOpen(id) ||
           NativeImGui.IsWindowHovered() &&
           !NativeImGui.IsAnyItemHovered() &&
           NativeImGui.IsMouseReleased(Inno.Native.ImGui.ImGuiMouseButton.Right);

    /// <summary>
    /// Draws the complete editor main menu bar for the supplied menu context.
    /// </summary>
    /// <param name="interaction">
    /// The main-menu interaction area.
    /// </param>
    public static void MainMenu(EditorInteraction interaction)
    {
        if (!NativeImGui.BeginMainMenuBar())
            return;
        try
        {
            DrawItems(interaction, interaction.BuildMenu().items);
            DrawCenteredToolbar(interaction, interaction.BuildToolbar().items);
        }
        finally
        {
            NativeImGui.EndMainMenuBar();
        }
    }

    private static void DrawCenteredToolbar(
        EditorInteraction interaction,
        IReadOnlyList<EditorToolbarItem> items)
    {
        if (items.Count == 0)
            return;
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float extent = NativeImGui.GetFrameHeight();
        float width = extent * items.Count + style.ItemSpacing.X * (items.Count - 1);
        float centeredOffset = (NativeImGui.GetWindowWidth() - width) * 0.5f;
        float minimumOffset = NativeImGui.GetCursorPosX() + style.ItemSpacing.X;
        NativeImGui.SameLine(MathF.Max(centeredOffset, minimumOffset), 0f);

        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
                NativeImGui.SameLine(0f, style.ItemSpacing.X);
            DrawToolbarItem(interaction, items[i], new Vector2(extent, extent));
        }
    }

    private static void DrawToolbarItem(
        EditorInteraction interaction,
        EditorToolbarItem item,
        Vector2 size)
    {
        bool disabled = !item.status.isEnabled;
        if (disabled)
            NativeImGui.BeginDisabled(true);
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        bool pressed = NativeImGui.InvisibleButton($"##editor_toolbar_{item.actionId}", size);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        if (disabled)
            NativeImGui.EndDisabled();

        if (item.status.isChecked || hovered || active)
        {
            Vector4 background = active
                ? EditorPalette.accentActive
                : hovered
                    ? EditorPalette.accentHovered
                    : EditorPalette.accent;
            NativeImGui.GetWindowDrawList().AddRectFilled(
                minimum,
                minimum + size,
                NativeImGui.ColorConvertFloat4ToU32(background),
                NativeImGui.GetStyle().FrameRounding);
        }

        uint color = NativeImGui.GetColorU32(disabled ? ImGuiCol.TextDisabled : ImGuiCol.Text);
        EditorWidget.AddGlyphCentered(
            NativeImGui.GetWindowDrawList(),
            NativeImGui.GetFont(),
            NativeImGui.GetFontSize(),
            GetToolbarIcon(item.icon),
            minimum + size * 0.5f,
            color);

        if (hovered && EditorWidget.BeginMenuTooltip())
        {
            string tooltip = interaction.TryGetShortcut(item.actionId, out HotKeyGesture shortcut)
                ? $"{item.tooltip} ({shortcut})"
                : item.tooltip;
            NativeImGui.TextUnformatted(tooltip);
            EditorWidget.EndMenuTooltip();
        }
        if (pressed && item.status.isEnabled)
            interaction.Enqueue(item.actionId);
    }

    private static string GetToolbarIcon(EditorToolbarIcon icon)
        => icon switch
        {
            EditorToolbarIcon.Play => ImGuiIcon.Play,
            EditorToolbarIcon.Stop => ImGuiIcon.Stop,
            EditorToolbarIcon.Pause => ImGuiIcon.Pause,
            EditorToolbarIcon.Step => ImGuiIcon.ForwardStep,
            EditorToolbarIcon.Edit => ImGuiIcon.Pen,
            _ => string.Empty
        };

    /// <summary>
    /// Recursively draws resolved menu nodes into the currently open popup or menu.
    /// </summary>
    /// <param name="interaction">
    /// The interaction used to resolve shortcuts and enqueue selected actions.
    /// </param>
    /// <param name="items">
    /// The immutable menu nodes to draw in display order.
    /// </param>
    public static void DrawItems(EditorInteraction interaction, IReadOnlyList<EditorMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (int i = 0; i < items.Count; i++)
        {
            EditorMenuItem item = items[i];
            // A separator groups adjacent commands; it is never meaningful before the first
            // visible item of a popup or submenu. Enforcing that here keeps every menu surface
            // consistent even when a type-specific query hides the commands that preceded it.
            if (i > 0 && item.separatorBefore)
                NativeImGui.Separator();
            if (item.children.Count > 0)
            {
                if (!NativeImGui.BeginMenu(item.label, item.status.isEnabled))
                    continue;
                try
                {
                    DrawItems(interaction, item.children);
                }
                finally
                {
                    NativeImGui.EndMenu();
                }
                continue;
            }

            string shortcut = interaction.TryGetShortcut(item.actionId, out HotKeyGesture gesture)
                ? gesture.ToString()
                : string.Empty;
            if (NativeImGui.MenuItem(
                    item.label,
                    shortcut,
                    item.status.isChecked,
                    item.status.isEnabled))
            {
                interaction.Enqueue(item.actionId, item.argument);
            }
        }
    }

    /// <summary>
    /// Draws matching leaf commands as a flat searchable list.
    /// </summary>
    /// <param name="interaction">
    /// The interaction used to enqueue selected actions.
    /// </param>
    /// <param name="items">
    /// The immutable menu tree whose leaves should be searched.
    /// </param>
    /// <param name="search">
    /// The case-insensitive text matched against each slash-delimited leaf path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a command was selected.
    /// </returns>
    public static bool DrawSearchItems(
        EditorInteraction interaction,
        IReadOnlyList<EditorMenuItem> items,
        string search)
    {
        ArgumentNullException.ThrowIfNull(items);
        return DrawSearchItems(interaction, items, search ?? string.Empty, string.Empty);
    }

    private static bool DrawSearchItems(
        EditorInteraction interaction,
        IReadOnlyList<EditorMenuItem> items,
        string search,
        string parentPath)
    {
        for (int i = 0; i < items.Count; i++)
        {
            EditorMenuItem item = items[i];
            string path = string.IsNullOrEmpty(parentPath)
                ? item.label
                : $"{parentPath}/{item.label}";
            if (item.children.Count > 0)
            {
                if (DrawSearchItems(interaction, item.children, search, path))
                    return true;
                continue;
            }
            if (!item.status.isEnabled ||
                !string.IsNullOrWhiteSpace(search) &&
                path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (!NativeImGui.Selectable(path))
                continue;
            interaction.Enqueue(item.actionId, item.argument);
            return true;
        }
        return false;
    }
}
