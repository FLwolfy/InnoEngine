using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Displays logs from <see cref="EditorLogBuffer"/> with filtering/collapse/detail view.
/// </summary>
public sealed class LogPanel : EditorPanel
{
    private const string C_ICON_BUG = ImGuiIcon.Bug;
    private const string C_ICON_INFO = ImGuiIcon.CircleInfo;
    private const string C_ICON_WARN = ImGuiIcon.TriangleExclamation;
    private const string C_ICON_ERROR = ImGuiIcon.CircleXmark;
    private const string C_ICON_FATAL = ImGuiIcon.SkullCrossbones;
    private const string C_ICON_DEFAULT = ImGuiIcon.FileLines;

    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();
    private readonly float[] m_levelTokenW = new float[Enum.GetValues<LogLevel>().Length];
    private readonly HashSet<long> m_openEntries = [];

    private bool m_collapse = true;
    private bool m_levelTokenWValid;
    private float m_levelTokenWFontSize = -1f;
    private int m_lastSnapshotCount = -1;
    private bool m_requestScrollToBottom = true;
    private bool m_lastRenderedCollapse = true;

    /// <summary>
    /// Creates the panel.
    /// </summary>
    public LogPanel()
        : base("log.console", "Log")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        NativeImGui.BeginChild("LogChild", Vector2.Zero);
        DrawToolbar(context);
        NativeImGui.Separator();
        DrawLogRegion(context);
        NativeImGui.EndChild();
    }

    private void DrawToolbar(EditorContext context)
    {
        bool collapseChanged = NativeImGui.Checkbox("Collapse", ref m_collapse);
        if (collapseChanged)
        {
            m_requestScrollToBottom = true;
        }

        NativeImGui.SameLine();
        bool filterChanged = DrawFilterCombo();
        if (filterChanged)
        {
            m_requestScrollToBottom = true;
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button("Clear"))
        {
            context.logs.Clear();
            m_lastSnapshotCount = 0;
            m_requestScrollToBottom = true;
        }
    }

    private bool DrawFilterCombo()
    {
        if (!NativeImGui.BeginCombo("##LogFilter", "Filter", ImGuiComboFlags.WidthFitPreview))
        {
            return false;
        }

        bool changed = false;
        for (int i = 0; i < m_levels.Length; i++)
        {
            LogLevel level = m_levels[i];
            bool selected = m_filterLevels.Contains(level);
            if (!NativeImGui.Checkbox(level.ToString(), ref selected))
            {
                continue;
            }

            if (selected)
            {
                m_filterLevels.Add(level);
            }
            else
            {
                m_filterLevels.Remove(level);
            }

            changed = true;
        }

        NativeImGui.EndCombo();
        return changed;
    }

    private void DrawLogRegion(EditorContext context)
    {
        NativeImGui.BeginChild("LogRegion", Vector2.Zero);

        LogEntry[] entries = context.logs.Snapshot();
        int previousSnapshotCount = m_lastSnapshotCount;
        bool hasNewEntries = previousSnapshotCount >= 0 && entries.Length > previousSnapshotCount;
        if (entries.Length != previousSnapshotCount)
        {
            m_lastSnapshotCount = entries.Length;
        }

        bool scrollAtBottom = NativeImGui.GetScrollY() >= NativeImGui.GetScrollMaxY() - 1f;
        bool shouldScrollToBottom = m_requestScrollToBottom || (hasNewEntries && scrollAtBottom);
        DrawEntries(entries);
        m_lastRenderedCollapse = m_collapse;

        if (shouldScrollToBottom)
        {
            NativeImGui.SetScrollHereY(1f);
            m_requestScrollToBottom = false;
        }

        NativeImGui.EndChild();
    }

    private void DrawEntries(LogEntry[] entries)
    {
        if (entries.Length == 0)
        {
            NativeImGui.BeginDisabled(true);
            NativeImGui.TextUnformatted("No logs yet.");
            NativeImGui.EndDisabled();
            return;
        }

        List<LogEntry> visibleEntries = [];
        for (int i = 0; i < entries.Length; i++)
        {
            LogEntry entry = entries[i];
            if (m_filterLevels.Contains(entry.level))
            {
                visibleEntries.Add(entry);
            }
        }

        if (visibleEntries.Count == 0)
        {
            NativeImGui.BeginDisabled(true);
            NativeImGui.TextUnformatted("All logs are filtered out.");
            NativeImGui.EndDisabled();
            return;
        }

        int start = 0;
        bool collapseModeChanged = m_collapse != m_lastRenderedCollapse;
        bool switchedToCollapse = collapseModeChanged && m_collapse;
        bool wasCollapseLastFrame = m_lastRenderedCollapse;

        while (start < visibleEntries.Count)
        {
            int end = start;
            while (end + 1 < visibleEntries.Count && IsSameEntryIgnoreTime(visibleEntries[end + 1], visibleEntries[end]))
            {
                end++;
            }

            if (m_collapse)
            {
                List<long> runEntryIds = [];
                for (int i = start; i <= end; i++)
                {
                    runEntryIds.Add(visibleEntries[i].time.Ticks);
                }

                long latestEntryId = runEntryIds[^1];
                bool latestEntryOpen = m_openEntries.Contains(latestEntryId);
                bool anyOpenInRun = false;
                for (int i = 0; i < runEntryIds.Count; i++)
                {
                    if (m_openEntries.Contains(runEntryIds[i]))
                    {
                        anyOpenInRun = true;
                        break;
                    }
                }

                if (latestEntryOpen)
                {
                    for (int i = 0; i < runEntryIds.Count; i++)
                    {
                        long runEntryId = runEntryIds[i];
                        if (runEntryId != latestEntryId)
                        {
                            m_openEntries.Remove(runEntryId);
                        }
                    }
                }
                else if (anyOpenInRun)
                {
                    // Rule 6: while already in collapse mode, expanded state must follow the latest entry.
                    bool shouldPromoteLatest = wasCollapseLastFrame && !switchedToCollapse;
                    for (int i = 0; i < runEntryIds.Count; i++)
                    {
                        m_openEntries.Remove(runEntryIds[i]);
                    }

                    // Rule 5: when switching from non-collapse to collapse, only latest-open keeps collapse expanded.
                    if (shouldPromoteLatest)
                    {
                        m_openEntries.Add(latestEntryId);
                    }
                }

                DrawLogEntry(visibleEntries[end], end - start + 1, runEntryIds, collapseView: true);
            }
            else
            {
                for (int i = start; i <= end; i++)
                {
                    long entryId = visibleEntries[i].time.Ticks;
                    DrawLogEntry(visibleEntries[i], 1, [entryId], collapseView: false);
                }
            }

            start = end + 1;
        }
    }

    private void DrawLogEntry(LogEntry entry, int repeatCount, IReadOnlyList<long> runEntryIds, bool collapseView)
    {
        (Vector4 levelColor, string levelIcon) = GetLevelVisual(entry.level);

        long entryId = entry.time.Ticks;
        int rowId = entryId.GetHashCode();
        NativeImGui.PushID(rowId);
        bool isOpen = m_openEntries.Contains(entryId);
        if (!isOpen)
        {
            Vector4 collapsedCardBg = GetCollapsedBgColor();
            Vector4 collapsedCardBorder = GetCollapsedBorderColor();
            NativeImGui.PushStyleColor(ImGuiCol.FrameBg, collapsedCardBg);
            NativeImGui.PushStyleColor(ImGuiCol.Border, collapsedCardBorder);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 2f));

            ImGuiChildFlags collapsedChildFlags = ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY;
            ImGuiWindowFlags collapsedChildWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;
            if (NativeImGui.BeginChild("##LogEntryCard", new Vector2(0f, 0f), collapsedChildFlags, collapsedChildWindowFlags))
            {
                DrawHeader(entry, repeatCount, levelColor, levelIcon, false, collapsedCardBg, out bool toggled);
                if (toggled)
                {
                    if (collapseView)
                    {
                        for (int i = 0; i < runEntryIds.Count; i++)
                        {
                            m_openEntries.Remove(runEntryIds[i]);
                        }

                        m_openEntries.Add(entryId);
                    }
                    else
                    {
                        m_openEntries.Add(entryId);
                    }
                }
            }

            NativeImGui.EndChild();
            NativeImGui.PopStyleVar(3);
            NativeImGui.PopStyleColor(2);
            NativeImGui.Dummy(new Vector2(0f, 1f));
            NativeImGui.PopID();
            return;
        }

        Vector4 cardBg = GetExpandedBgColor(levelColor);
        Vector4 cardBorder = LerpColor(new Vector4(0.24f, 0.24f, 0.24f, 1f), levelColor, 0.20f);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, cardBg);
        NativeImGui.PushStyleColor(ImGuiCol.Border, cardBorder);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 2f));

        ImGuiChildFlags childFlags = ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY;
        ImGuiWindowFlags childWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;
        if (NativeImGui.BeginChild("##LogEntryCard", new Vector2(0f, 0f), childFlags, childWindowFlags))
        {
            DrawHeader(entry, repeatCount, levelColor, levelIcon, true, cardBg, out bool toggled);
            if (toggled)
            {
                if (collapseView)
                {
                    for (int i = 0; i < runEntryIds.Count; i++)
                    {
                        m_openEntries.Remove(runEntryIds[i]);
                    }
                }
                else
                {
                    m_openEntries.Remove(entryId);
                }
            }

            NativeImGui.PushStyleColor(ImGuiCol.Separator, LerpColor(cardBg, Vector4.One, 0.12f));
            NativeImGui.Separator();
            NativeImGui.PopStyleColor();
            DrawDetail(entry);
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleVar(3);
        NativeImGui.PopStyleColor(2);
        NativeImGui.Dummy(new Vector2(0f, 2f));
        NativeImGui.PopID();
    }

    private static void DrawHeader(
        LogEntry entry,
        int repeatCount,
        Vector4 levelColor,
        string levelIcon,
        bool isOpen,
        Vector4 headerBgColor,
        out bool toggled)
    {
        toggled = false;
        string content = isOpen ? entry.message : GetFirstLine(entry.message);
        string repeatText = repeatCount > 1 ? $" (x{repeatCount})" : string.Empty;
        string prefix = $"{levelIcon} [{entry.level}] ";
        string toggleText = isOpen ? "▾" : "▸";
        ImGuiStylePtr style = NativeImGui.GetStyle();
        const float togglePadX = 2f;
        const float togglePadY = 1f;
        float toggleW = NativeImGui.CalcTextSize(toggleText).X + togglePadX * 2f;
        float prefixW = NativeImGui.CalcTextSize(prefix).X + toggleW + style.ItemInnerSpacing.X;
        float suffixW = NativeImGui.CalcTextSize(repeatText).X;
        float suffixColW = MathF.Max(1f, suffixW);
        float tableOuterW = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float contentW = MathF.Max(1f, tableOuterW - prefixW - suffixColW);
        if (!isOpen)
        {
            content = FitTextWithEllipsis(GetFirstLine(entry.message), contentW);
        }
        Vector2 tableOuterSize = new(tableOuterW, 0f);
        ImGuiTableFlags flags =
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.NoHostExtendX |
            ImGuiTableFlags.NoPadOuterX |
            ImGuiTableFlags.NoPadInnerX |
            ImGuiTableFlags.NoSavedSettings;

        if (NativeImGui.BeginTable("##HeaderTable", 3, flags, tableOuterSize))
        {
            NativeImGui.TableSetupColumn("##prefix", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, prefixW);
            NativeImGui.TableSetupColumn("##content", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, contentW);
            NativeImGui.TableSetupColumn("##suffix", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, suffixColW);

            NativeImGui.TableNextRow();
            if (isOpen)
            {
                uint headerBgU32 = NativeImGui.ColorConvertFloat4ToU32(headerBgColor);
                NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, headerBgU32);
            }

            _ = NativeImGui.TableSetColumnIndex(0);
            Vector4 toggleBg = NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.Button, 0.82f));
            Vector4 toggleHovered = NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.ButtonHovered, 0.82f));
            Vector4 toggleActive = NativeImGui.ColorConvertU32ToFloat4(NativeImGui.GetColorU32(ImGuiCol.ButtonActive, 0.82f));
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(togglePadX, togglePadY));
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            NativeImGui.PushStyleColor(ImGuiCol.Button, toggleBg);
            NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, toggleHovered);
            NativeImGui.PushStyleColor(ImGuiCol.ButtonActive, toggleActive);
            if (NativeImGui.SmallButton($"{toggleText}##HeaderToggle"))
            {
                toggled = true;
            }
            NativeImGui.PopStyleColor(3);
            NativeImGui.PopStyleVar(2);
            NativeImGui.SameLine();
            NativeImGui.PushStyleColor(ImGuiCol.Text, levelColor);
            NativeImGui.TextUnformatted(prefix);
            NativeImGui.PopStyleColor();

            _ = NativeImGui.TableSetColumnIndex(1);
            if (isOpen)
            {
                NativeImGui.PushTextWrapPos(0f);
                NativeImGui.TextUnformatted(content);
                NativeImGui.PopTextWrapPos();
            }
            else
            {
                NativeImGui.TextUnformatted(content);
            }

            _ = NativeImGui.TableSetColumnIndex(2);
            if (!string.IsNullOrEmpty(repeatText))
            {
                float remainW = NativeImGui.GetContentRegionAvail().X;
                float suffixOffsetX = MathF.Max(0f, remainW - suffixW);
                NativeImGui.SetCursorPosX(NativeImGui.GetCursorPosX() + suffixOffsetX);
                NativeImGui.TextUnformatted(repeatText);
            }

            NativeImGui.EndTable();
        }
    }

    private static void DrawDetail(LogEntry entry)
    {
        string timeText = entry.time.ToString("HH:mm:ss");
        string sourceText = entry.source.ToString();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            sourceText = "-";
        }

        string fileText = string.IsNullOrWhiteSpace(entry.file) ? "-" : entry.file;
        string lineText = entry.line.ToString();
        string fileWithLineText = entry.line > 0 ? $"{fileText}:{lineText}" : fileText;

        if (NativeImGui.BeginTable("##LogEntryDetails", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
        {
            NativeImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
            NativeImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize, 1f);
            DrawDetailFieldRow("File:", fileWithLineText);
            DrawDetailFieldRow("Source:", sourceText);
            DrawDetailFieldRow("Time:", timeText);
            NativeImGui.EndTable();
        }
    }

    private static void DrawDetailFieldRow(string label, string value)
    {
        NativeImGui.TableNextRow();
        _ = NativeImGui.TableSetColumnIndex(0);
        NativeImGui.TextUnformatted(label);
        _ = NativeImGui.TableSetColumnIndex(1);
        NativeImGui.PushTextWrapPos(0f);
        NativeImGui.TextUnformatted(value);
        NativeImGui.PopTextWrapPos();
    }
    
    private static (Vector4 color, string icon) GetLevelVisual(LogLevel level)
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

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            1f);
    }

    private static bool IsSameEntryIgnoreTime(in LogEntry a, in LogEntry b)
    {
        return a.level == b.level
            && a.source.Equals(b.source)
            && string.Equals(a.category, b.category, StringComparison.Ordinal)
            && string.Equals(a.message, b.message, StringComparison.Ordinal)
            && string.Equals(a.file, b.file, StringComparison.Ordinal)
            && a.line == b.line;
    }

    private static Vector4 GetCollapsedBgColor()
    {
        uint bgU32 = NativeImGui.GetColorU32(ImGuiCol.Button, 0.55f);
        return NativeImGui.ColorConvertU32ToFloat4(bgU32);
    }

    private static Vector4 GetCollapsedBorderColor()
    {
        uint borderU32 = NativeImGui.GetColorU32(ImGuiCol.Border, 0.65f);
        return NativeImGui.ColorConvertU32ToFloat4(borderU32);
    }

    private static Vector4 GetExpandedBgColor(Vector4 levelColor)
    {
        return LerpColor(new Vector4(0.10f, 0.10f, 0.10f, 1f), levelColor, 0.12f);
    }
    
    private static string GetFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\n' or '\r')
            {
                return text[..i];
            }
        }

        return text;
    }

    private static string FitTextWithEllipsis(string text, float maxWidth)
    {
        const string ellipsis = "...";
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (maxWidth <= 1f)
        {
            return ellipsis;
        }

        if (NativeImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        float ellipsisW = NativeImGui.CalcTextSize(ellipsis).X;
        if (ellipsisW >= maxWidth)
        {
            return ellipsis;
        }

        float maxTextW = maxWidth - ellipsisW;
        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (NativeImGui.CalcTextSize(text[..mid]).X <= maxTextW)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (lo <= 0)
        {
            return ellipsis;
        }

        return text[..lo] + ellipsis;
    }
}
