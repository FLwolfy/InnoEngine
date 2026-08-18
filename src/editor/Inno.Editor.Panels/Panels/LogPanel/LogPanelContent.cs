using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

internal sealed class LogPanelContent
{
    private const string C_ICON_BUG = ImGuiIcon.Bug;
    private const string C_ICON_INFO = ImGuiIcon.CircleInfo;
    private const string C_ICON_WARN = ImGuiIcon.TriangleExclamation;
    private const string C_ICON_ERROR = ImGuiIcon.CircleXmark;
    private const string C_ICON_FATAL = ImGuiIcon.SkullCrossbones;
    private const string C_ICON_DEFAULT = ImGuiIcon.FileLines;

    internal void DrawDisabledHint(string text)
    {
        NativeImGui.BeginDisabled(true);
        NativeImGui.TextUnformatted(text);
        NativeImGui.EndDisabled();
    }

    internal List<LogEntry> CollectVisibleEntries(
        LogEntry[] entries,
        IReadOnlySet<LogLevel> filterLevels)
    {
        List<LogEntry> visibleEntries = [];
        for (int i = 0; i < entries.Length; i++)
        {
            if (filterLevels.Contains(entries[i].level))
                visibleEntries.Add(entries[i]);
        }
        return visibleEntries;
    }

    internal List<long> CollectRunEntryIds(List<LogEntry> visibleEntries, int start, int end)
    {
        List<long> runEntryIds = [];
        for (int i = start; i <= end; i++)
            runEntryIds.Add(visibleEntries[i].time.Ticks);
        return runEntryIds;
    }

    internal bool ContainsAnyOpen(
        IReadOnlyList<long> runEntryIds,
        IReadOnlySet<long> openEntries)
    {
        for (int i = 0; i < runEntryIds.Count; i++)
        {
            if (openEntries.Contains(runEntryIds[i]))
                return true;
        }
        return false;
    }

    internal void KeepOnlyLatestOpen(
        IReadOnlyList<long> runEntryIds,
        long latestEntryId,
        HashSet<long> openEntries)
    {
        for (int i = 0; i < runEntryIds.Count; i++)
        {
            if (runEntryIds[i] != latestEntryId)
                openEntries.Remove(runEntryIds[i]);
        }
    }

    internal void CloseAll(IReadOnlyList<long> runEntryIds, HashSet<long> openEntries)
    {
        for (int i = 0; i < runEntryIds.Count; i++)
            openEntries.Remove(runEntryIds[i]);
    }

    internal (Vector4 color, string icon) GetLevelVisual(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => (new Vector4(0.80f, 0.90f, 0.85f, 1f), C_ICON_BUG),
            LogLevel.Info => (new Vector4(0.20f, 1f, 0.20f, 1f), C_ICON_INFO),
            LogLevel.Warn => (new Vector4(1f, 1f, 0.20f, 1f), C_ICON_WARN),
            LogLevel.Error => (new Vector4(1f, 0.20f, 0.20f, 1f), C_ICON_ERROR),
            LogLevel.Fatal => (new Vector4(1f, 0.20f, 1f, 1f), C_ICON_FATAL),
            _ => (Vector4.One, C_ICON_DEFAULT)
        };
    }

    internal Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            1f);
    }

    internal bool IsSameEntryIgnoreTime(in LogEntry a, in LogEntry b)
        => a.level == b.level &&
           a.source.Equals(b.source) &&
           string.Equals(a.category, b.category, StringComparison.Ordinal) &&
           string.Equals(a.message, b.message, StringComparison.Ordinal) &&
           string.Equals(a.file, b.file, StringComparison.Ordinal) &&
           a.line == b.line;

    internal Vector4 GetCollapsedBgColor()
        => NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.Button, 0.55f));

    internal Vector4 GetCollapsedBorderColor()
        => NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.Border, 0.65f));

    internal Vector4 GetExpandedBgColor(Vector4 levelColor)
        => LerpColor(new Vector4(0.10f, 0.10f, 0.10f, 1f), levelColor, 0.12f);

    internal string GetFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n' or '\r')
                return text[..i];
        }
        return text;
    }

    internal string FitTextWithEllipsis(string text, float maxWidth)
    {
        const string c_ellipsis = "...";
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (maxWidth <= 1f)
            return c_ellipsis;
        if (NativeImGui.CalcTextSize(text).X <= maxWidth)
            return text;
        float ellipsisWidth = NativeImGui.CalcTextSize(c_ellipsis).X;
        if (ellipsisWidth >= maxWidth)
            return c_ellipsis;

        float maxTextWidth = maxWidth - ellipsisWidth;
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (NativeImGui.CalcTextSize(text[..middle]).X <= maxTextWidth)
                low = middle;
            else
                high = middle - 1;
        }
        return low <= 0 ? c_ellipsis : text[..low] + c_ellipsis;
    }
}
