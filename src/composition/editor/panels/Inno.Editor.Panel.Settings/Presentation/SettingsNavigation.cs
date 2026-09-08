using System;
using System.Collections.Generic;

namespace Inno.Editor.Panel.Settings;

internal sealed class SettingsNavigation
{
    private readonly Stack<string> m_back = new();
    private readonly Stack<string> m_forward = new();

    internal string currentPath { get; private set; } = string.Empty;

    internal bool canGoBack => m_back.Count > 0;

    internal bool canGoForward => m_forward.Count > 0;

    internal void Reset(string path)
    {
        currentPath = path ?? string.Empty;
        m_back.Clear();
        m_forward.Clear();
    }

    internal void NavigateTo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.Equals(currentPath, path, StringComparison.Ordinal))
            return;
        if (!string.IsNullOrEmpty(currentPath))
            m_back.Push(currentPath);
        currentPath = path;
        m_forward.Clear();
    }

    internal void Replace(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        currentPath = path;
    }

    internal void GoBack()
    {
        if (!m_back.TryPop(out string? path))
            return;
        if (!string.IsNullOrEmpty(currentPath))
            m_forward.Push(currentPath);
        currentPath = path;
    }

    internal void GoForward()
    {
        if (!m_forward.TryPop(out string? path))
            return;
        if (!string.IsNullOrEmpty(currentPath))
            m_back.Push(currentPath);
        currentPath = path;
    }
}
