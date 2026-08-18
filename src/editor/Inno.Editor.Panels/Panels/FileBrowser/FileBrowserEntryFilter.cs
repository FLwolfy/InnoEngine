using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets.File;

namespace Inno.Editor.Panels;

/// <summary>
/// Defines editor-only filesystem noise hidden by the project browser.
/// </summary>
internal static class FileBrowserEntryFilter
{
    private static readonly HashSet<string> S_IGNORED_FILE_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        ".directory",
        "Desktop.ini",
        "ehthumbs.db",
        "Icon\r",
        "Thumbs.db"
    };

    private static readonly HashSet<string> S_IGNORED_DIRECTORY_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".idea",
        ".svn",
        ".vs",
        ".vscode",
        "__MACOSX"
    };

    private static readonly string[] S_IGNORED_PREFIXES =
    [
        ".#",
        "._",
        "~$"
    ];

    private static readonly string[] S_IGNORED_SUFFIXES =
    [
        ".bak",
        ".orig",
        ".swo",
        ".swp",
        ".temp",
        ".tmp",
        "~"
    ];

    internal static bool IsVisible(AssetFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string name = Path.GetFileName(entry.relativePath);
        if (entry.isDirectory && S_IGNORED_DIRECTORY_NAMES.Contains(name))
            return false;
        if (!entry.isDirectory && S_IGNORED_FILE_NAMES.Contains(name))
            return false;

        for (int i = 0; i < S_IGNORED_PREFIXES.Length; i++)
        {
            if (name.StartsWith(S_IGNORED_PREFIXES[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        for (int i = 0; i < S_IGNORED_SUFFIXES.Length; i++)
        {
            if (name.EndsWith(S_IGNORED_SUFFIXES[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
