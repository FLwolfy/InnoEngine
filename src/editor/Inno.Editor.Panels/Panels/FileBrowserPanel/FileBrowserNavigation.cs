using System;
using System.Collections.Generic;

using Inno.Assets.File;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Engine.Scene;
using static Inno.Editor.Panels.FileBrowserUtility;

namespace Inno.Editor.Panels;

internal sealed class FileBrowserNavigation
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
        if (string.Equals(context.selection.currentDirectory, directory, StringComparison.Ordinal))
            return;

        m_backHistory.Push(NormalizePath(context.selection.currentDirectory));
        m_forwardHistory.Clear();
        ApplyDirectory(context, directory);
        if (selectedPathAfterNavigation is not null)
            context.selection.SetSelectedPath(selectedPathAfterNavigation);
    }

    internal void GoBack(EditorContext context)
    {
        if (m_backHistory.Count == 0)
            return;
        m_forwardHistory.Push(NormalizePath(context.selection.currentDirectory));
        ApplyDirectory(context, m_backHistory.Pop());
    }

    internal void GoForward(EditorContext context)
    {
        if (m_forwardHistory.Count == 0)
            return;
        m_backHistory.Push(NormalizePath(context.selection.currentDirectory));
        ApplyDirectory(context, m_forwardHistory.Pop());
    }

    private void ApplyDirectory(EditorContext context, string directory)
    {
        m_historyCurrent = NormalizePath(directory);
        context.selection.SetCurrentDirectory(m_historyCurrent);
        context.selection.SetSelectedPath(string.Empty);
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
        if (!string.Equals(entry.extension, ".innoscene", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            GameScene scene = context.sceneWorkspace.OpenScene(entry.relativePath);
            context.selection.Select(scene);
        }
        catch (Exception exception)
        {
            Log.Error("Failed to open scene asset '{0}': {1}", entry.relativePath, exception);
        }
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
