using Inno.Editor.Panel.FileBrowser;


using System;
using System.Collections.Generic;

using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions.Actions;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class FileBrowserNavigation(AssetEditorModule assets)
{
    private readonly Stack<string> m_backHistory = [];
    private readonly Stack<string> m_forwardHistory = [];

    private string m_historyCurrent = string.Empty;

    internal bool canGoBack => m_backHistory.Count > 0;

    internal bool canGoForward => m_forwardHistory.Count > 0;

    internal void NavigateTo(
        EditorContext context,
        string directory,
        string? selectedPathAfterNavigation = null)
    {
        directory = NormalizePath(directory);
        if (string.Equals(assets.browser.currentDirectory, directory, StringComparison.Ordinal))
            return;

        m_backHistory.Push(NormalizePath(assets.browser.currentDirectory));
        m_forwardHistory.Clear();
        ApplyDirectory(context, directory);
        if (selectedPathAfterNavigation is not null)
            assets.browser.Select(context, selectedPathAfterNavigation);
    }

    internal void GoBack(EditorContext context)
    {
        if (m_backHistory.Count == 0)
            return;
        m_forwardHistory.Push(NormalizePath(assets.browser.currentDirectory));
        ApplyDirectory(context, m_backHistory.Pop());
    }

    internal void GoForward(EditorContext context)
    {
        if (m_forwardHistory.Count == 0)
            return;
        m_backHistory.Push(NormalizePath(assets.browser.currentDirectory));
        ApplyDirectory(context, m_forwardHistory.Pop());
    }

    private void ApplyDirectory(EditorContext context, string directory)
    {
        m_historyCurrent = NormalizePath(directory);
        assets.browser.SetCurrentDirectory(m_historyCurrent);
        assets.browser.Select(context, string.Empty);
    }

    internal void OpenEntry(
        EditorContext context,
        AssetFileEntry entry,
        FileBrowserTree tree)
    {
        if (entry.isDirectory)
        {
            tree.RequestOpenTreeToPath(entry.relativePath);
            NavigateTo(context, entry.relativePath, entry.relativePath);
            return;
        }
        _ = assets.interactions
            .For(FileBrowserAreas.Browser, entry)
            .Execute(EditorActions.Open);
    }

    internal void SyncExternalDirectoryChange(string directory)
    {
        directory = NormalizePath(directory);
        if (string.Equals(m_historyCurrent, directory, StringComparison.Ordinal))
            return;
        m_historyCurrent = directory;
        m_backHistory.Clear();
        m_forwardHistory.Clear();
    }
}
