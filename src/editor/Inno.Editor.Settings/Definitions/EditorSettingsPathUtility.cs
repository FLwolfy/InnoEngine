using System;

namespace Inno.Editor.Settings;

internal static class EditorSettingsPathUtility
{
    internal static (string Path, string ParentPath, string Label) Parse(string path)
    {
        string normalized = Normalize(path);
        int separator = normalized.LastIndexOf('/');
        return separator < 0
            ? (normalized, string.Empty, normalized)
            : (normalized, normalized[..separator], normalized[(separator + 1)..]);
    }

    internal static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] segments = path.Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || Array.Exists(segments, static segment => segment.Length == 0))
            throw new ArgumentException("Settings paths cannot contain empty segments.", nameof(path));
        return string.Join('/', segments);
    }
}
