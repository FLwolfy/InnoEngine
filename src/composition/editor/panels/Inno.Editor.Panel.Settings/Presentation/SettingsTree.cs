using System;
using System.Collections.Generic;

using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

internal sealed class SettingsTree
{
    private string? m_revealPath;

    internal void Reveal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        m_revealPath = path;
    }

    internal void Draw(
        IReadOnlyList<SettingsPage> pages,
        string query,
        string selectedPath,
        Action<SettingsPage> select)
    {
        string? revealPath = m_revealPath;
        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                if (Matches(pages[i], query))
                    DrawPage(pages[i], query, selectedPath, revealPath, select);
            }
        }
        finally
        {
            // Apply a navigation reveal to retained tree state once. Later frames do not force it,
            // so the user can immediately collapse the branch again.
            m_revealPath = null;
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
        string? revealPath,
        Action<SettingsPage> select)
    {
        bool hasVisibleChildren = false;
        for (int i = 0; i < page.children.Count; i++)
            hasVisibleChildren |= Matches(page.children[i], query);
        if ((!string.IsNullOrWhiteSpace(query) && hasVisibleChildren) ||
            (hasVisibleChildren && IsRevealed(page.path, revealPath)))
            EditorWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = EditorWidget.TreeNode(
            $"settings_tree_{page.path}",
            _ => NativeImGui.TextUnformatted(page.label),
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
                DrawPage(page.children[i], query, selectedPath, revealPath, select);
        }
        NativeImGui.TreePop();
    }

    private static bool IsRevealed(string pagePath, string? revealPath)
        => revealPath is not null &&
           (string.Equals(pagePath, revealPath, StringComparison.Ordinal) ||
            revealPath.StartsWith(pagePath + "/", StringComparison.Ordinal));

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
            SettingsField setting = page.settings[i];
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
                SettingsField setting = page.settings[settingIndex];
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
