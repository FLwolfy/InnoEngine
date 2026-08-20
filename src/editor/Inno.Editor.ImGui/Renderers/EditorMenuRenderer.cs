using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.Core.Menus;
using Inno.Editor.Core.Commands;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>Renders immutable editor menu models through ImGui.</summary>
public static class EditorMenuRenderer
{
    /// <summary>
    /// Draws a resolved right-click menu for the most recently submitted ImGui item.
    /// </summary>
    /// <param name="id">The stable popup identifier in the current ImGui ID scope.</param>
    /// <param name="context">The editor, menu surface, and optional operation target.</param>
    /// <returns><see langword="true"/> while the context popup is open and its items were drawn.</returns>
    public static bool ContextMenu(string id, EditorMenuContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(context);
        if (!ImGuiWidget.BeginContextMenu(id))
            return false;
        DrawItems(context, context.editorContext.BuildMenu(context.surface, context.target).items);
        ImGuiWidget.EndContextMenu();
        return true;
    }

    /// <summary>
    /// Draws the complete editor main menu bar for the supplied menu context.
    /// </summary>
    /// <param name="context">The main-menu surface and shared editor context.</param>
    public static void MainMenu(EditorMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!NativeImGui.BeginMainMenuBar())
            return;
        DrawItems(context, context.editorContext.BuildMenu(context.surface, context.target).items);
        NativeImGui.EndMainMenuBar();
    }

    /// <summary>
    /// Recursively draws resolved menu nodes into the currently open popup or menu.
    /// </summary>
    /// <param name="context">The menu context used to resolve shortcuts and enqueue selected actions.</param>
    /// <param name="items">The immutable menu nodes to draw in display order.</param>
    public static void DrawItems(EditorMenuContext context, IReadOnlyList<EditorMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(items);
        for (int i = 0; i < items.Count; i++)
        {
            EditorMenuItem item = items[i];
            if (item.separatorBefore)
                NativeImGui.Separator();
            if (item.children.Count > 0)
            {
                if (!NativeImGui.BeginMenu(item.label, item.status.isEnabled))
                    continue;
                DrawItems(context, item.children);
                NativeImGui.EndMenu();
                continue;
            }

            string shortcut = context.editorContext.TryGetShortcut(
                item.actionId,
                context.surface,
                out HotKeyGesture gesture)
                ? gesture.ToString()
                : string.Empty;
            if (NativeImGui.MenuItem(
                    item.label,
                    shortcut,
                    item.status.isChecked,
                    item.status.isEnabled))
            {
                context.editorContext.Enqueue(
                    item.actionId,
                    context.surface,
                    context.target,
                    item.argument);
            }
        }
    }

    /// <summary>Draws matching leaf commands as a flat searchable list.</summary>
    /// <param name="context">The menu context used to enqueue selected actions.</param>
    /// <param name="items">The immutable menu tree whose leaves should be searched.</param>
    /// <param name="search">The case-insensitive text matched against each slash-delimited leaf path.</param>
    /// <returns><see langword="true"/> when a command was selected.</returns>
    public static bool DrawSearchItems(
        EditorMenuContext context,
        IReadOnlyList<EditorMenuItem> items,
        string search)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(items);
        return DrawSearchItems(context, items, search ?? string.Empty, string.Empty);
    }

    private static bool DrawSearchItems(
        EditorMenuContext context,
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
                if (DrawSearchItems(context, item.children, search, path))
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
            context.editorContext.Enqueue(
                item.actionId,
                context.surface,
                context.target,
                item.argument);
            return true;
        }
        return false;
    }
}
