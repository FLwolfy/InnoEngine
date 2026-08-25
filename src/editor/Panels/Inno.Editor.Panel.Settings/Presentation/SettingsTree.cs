using System;
using System.Collections.Generic;

using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

internal sealed class SettingsTree
{
    internal void Draw(
        IReadOnlyList<SettingsPage> pages,
        string query,
        string selectedPath,
        Action<SettingsPage> select)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (Matches(pages[i], query))
                DrawPage(pages[i], query, selectedPath, select);
        }
    }

    internal static SettingsPage? FindPage(
        IReadOnlyList<SettingsPage> pages,
        string path)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            SettingsPage page = pages[i];
            if (string.Equals(page.path, path, StringComparison.Ordinal))
                return page;
            SettingsPage? nested = FindPage(page.children, path);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    internal static SettingsPage? FindFirstMatch(
        IReadOnlyList<SettingsPage> pages,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return pages.Count > 0 ? pages[0] : null;
        SettingsPage? fieldMatch = FindFirstFieldMatch(pages, query);
        if (fieldMatch is not null)
            return fieldMatch;
        for (int i = 0; i < pages.Count; i++)
        {
            if (MatchesSelf(pages[i], query))
                return pages[i];
            SettingsPage? nested = FindFirstMatch(pages[i].children, query);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static void DrawPage(
        SettingsPage page,
        string query,
        string selectedPath,
        Action<SettingsPage> select)
    {
        bool hasVisibleChildren = false;
        for (int i = 0; i < page.children.Count; i++)
            hasVisibleChildren |= Matches(page.children[i], query);
        if (!string.IsNullOrWhiteSpace(query) && hasVisibleChildren)
            EditorWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = EditorWidget.TreeNode(
            $"settings_tree_{page.path}",
            () => NativeImGui.TextUnformatted(page.label),
            new TreeNodeOptions
            {
                selected = string.Equals(page.path, selectedPath, StringComparison.Ordinal),
                isLeaf = !hasVisibleChildren,
                hideGuideLines = false
            });
        if (result.isClicked)
            select(page);
        if (!result.isOpen)
            return;
        for (int i = 0; i < page.children.Count; i++)
        {
            if (Matches(page.children[i], query))
                DrawPage(page.children[i], query, selectedPath, select);
        }
        NativeImGui.TreePop();
    }

    private static bool Matches(SettingsPage page, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        if (MatchesSelf(page, query))
            return true;
        for (int i = 0; i < page.children.Count; i++)
        {
            if (Matches(page.children[i], query))
                return true;
        }
        return false;
    }

    private static bool MatchesSelf(SettingsPage page, string query)
    {
        if (page.label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            page.path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            page.description.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        for (int i = 0; i < page.settings.Count; i++)
        {
            EditorSetting setting = page.settings[i];
            if (setting.label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (setting.section?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                setting.description.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static SettingsPage? FindFirstFieldMatch(
        IReadOnlyList<SettingsPage> pages,
        string query)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            SettingsPage page = pages[i];
            for (int settingIndex = 0; settingIndex < page.settings.Count; settingIndex++)
            {
                EditorSetting setting = page.settings[settingIndex];
                if (setting.label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (setting.section?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    setting.description.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return page;
                }
            }
            SettingsPage? nested = FindFirstFieldMatch(page.children, query);
            if (nested is not null)
                return nested;
        }
        return null;
    }
}
