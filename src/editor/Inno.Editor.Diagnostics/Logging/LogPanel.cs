using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Core.Panels;
using Inno.Editor.Diagnostics;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Diagnostics.Logging;

/// <summary>
/// Displays logs from <see cref="EditorLogBuffer"/> with filtering/collapse/detail view.
/// </summary>
[EditorPanel("diagnostics.log", "Log", order: 400)]
public sealed class LogPanel : EditorPanel
{
    #region State
    private readonly LogPanelContent m_content = new();
    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();
    private readonly HashSet<long> m_openEntries = [];

    private bool m_collapse = true;
    private long m_lastSnapshotVersion = -1;
    private bool m_requestScrollToBottom = true;
    private bool m_lastRenderedCollapse = true;
    private readonly DiagnosticsModule m_diagnostics;
    #endregion

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="diagnostics">The automatically discovered diagnostics module that owns the rolling log buffer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    public LogPanel(DiagnosticsModule diagnostics)
    {
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        NativeImGui.BeginChild("LogChild", Vector2.Zero);
        DrawToolbar(context);
        NativeImGui.Separator();
        DrawLogRegion(context);
        NativeImGui.EndChild();
    }
    #endregion

    #region Toolbar
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
            m_diagnostics.logs.Clear();
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
    #endregion

    #region Log Region
    private void DrawLogRegion(EditorContext context)
    {
        NativeImGui.BeginChild("LogRegion", Vector2.Zero);

        LogEntry[] entries = m_diagnostics.logs.Snapshot(out long snapshotVersion);
        bool hasNewEntries = m_lastSnapshotVersion >= 0 && snapshotVersion != m_lastSnapshotVersion;
        m_lastSnapshotVersion = snapshotVersion;

        bool scrollAtBottom = NativeImGui.GetScrollY() >=
                              NativeImGui.GetScrollMaxY() - ImGuiWidget.style.logAutoScrollTolerance;
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
    #endregion

    #region Entries
    private void DrawEntries(LogEntry[] entries)
    {
        if (entries.Length == 0)
        {
            m_content.DrawDisabledHint("No logs yet.");
            return;
        }

        List<LogEntry> visibleEntries = m_content.CollectVisibleEntries(entries, m_filterLevels);

        if (visibleEntries.Count == 0)
        {
            m_content.DrawDisabledHint("All logs are filtered out.");
            return;
        }

        int start = 0;
        bool collapseModeChanged = m_collapse != m_lastRenderedCollapse;
        bool switchedToCollapse = collapseModeChanged && m_collapse;
        bool wasCollapseLastFrame = m_lastRenderedCollapse;

        while (start < visibleEntries.Count)
        {
            int end = start;
            while (end + 1 < visibleEntries.Count &&
                   m_content.IsSameEntryIgnoreTime(visibleEntries[end + 1], visibleEntries[end]))
            {
                end++;
            }

            if (m_collapse)
            {
                List<long> runEntryIds = m_content.CollectRunEntryIds(visibleEntries, start, end);
                long latestEntryId = runEntryIds[^1];
                bool latestEntryOpen = m_openEntries.Contains(latestEntryId);
                bool anyOpenInRun = m_content.ContainsAnyOpen(runEntryIds, m_openEntries);

                if (latestEntryOpen)
                {
                    m_content.KeepOnlyLatestOpen(runEntryIds, latestEntryId, m_openEntries);
                }
                else if (anyOpenInRun)
                {
                    bool shouldPromoteLatest = wasCollapseLastFrame && !switchedToCollapse;
                    m_content.CloseAll(runEntryIds, m_openEntries);

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
    #endregion

    #region Entry Card
    private void DrawLogEntry(LogEntry entry, int repeatCount, IReadOnlyList<long> runEntryIds, bool collapseView)
    {
        (Vector4 levelColor, string levelIcon) = m_content.GetLevelVisual(entry.level);

        long entryId = entry.time.Ticks;
        int rowId = entryId.GetHashCode();
        NativeImGui.PushID(rowId);
        bool open = m_openEntries.Contains(entryId);
        if (!open)
        {
            Vector4 collapsedCardBg = m_content.GetCollapsedBgColor();
            Vector4 collapsedCardBorder = m_content.GetCollapsedBorderColor();
            NativeImGui.PushStyleColor(ImGuiCol.FrameBg, collapsedCardBg);
            NativeImGui.PushStyleColor(ImGuiCol.Border, collapsedCardBorder);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, ImGuiWidget.style.borderSize);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.framePadding);

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
            NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.logCollapsedSpacing));
            NativeImGui.PopID();
            return;
        }

        Vector4 cardBg = m_content.GetExpandedBgColor(levelColor);
        Vector4 cardBorder = EditorPalette.GetLogExpandedBorder(levelColor);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, cardBg);
        NativeImGui.PushStyleColor(ImGuiCol.Border, cardBorder);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, ImGuiWidget.style.borderSize);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.framePadding);

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

            NativeImGui.PushStyleColor(
                ImGuiCol.Separator,
                EditorPalette.GetLogSeparator(cardBg));
            NativeImGui.Separator();
            NativeImGui.PopStyleColor();
            DrawDetail(entry);
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleVar(3);
        NativeImGui.PopStyleColor(2);
        NativeImGui.Dummy(new Vector2(0f, ImGuiWidget.style.logExpandedSpacing));
        NativeImGui.PopID();
    }
    #endregion

    #region Entry Header / Detail
    private void DrawHeader(
        LogEntry entry,
        int repeatCount,
        Vector4 levelColor,
        string levelIcon,
        bool isOpen,
        Vector4 headerBgColor,
        out bool toggled)
    {
        toggled = false;
        ImGuiStylePtr style = NativeImGui.GetStyle();
        
        string content = isOpen ? entry.message : m_content.GetFirstLine(entry.message);
        string repeatText = repeatCount > 1 ? $" (x{repeatCount})" : string.Empty;
        string prefix = $"{levelIcon} [{entry.level}] ";
        string toggleText = isOpen ? "▼" : "▶";
        
        float toggleW = NativeImGui.CalcTextSize(toggleText).X + ImGuiWidget.style.logDisclosurePadding.X * 2f;
        float prefixW = NativeImGui.CalcTextSize(prefix).X + toggleW + style.ItemInnerSpacing.X;
        float suffixW = NativeImGui.CalcTextSize(repeatText).X;
        float suffixColW = MathF.Max(1f, suffixW);
        float tableOuterW = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float contentW = MathF.Max(1f, tableOuterW - prefixW - suffixColW);
        
        if (!isOpen)
        {
            content = m_content.FitTextWithEllipsis(m_content.GetFirstLine(entry.message), contentW);
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
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.logDisclosurePadding);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            NativeImGui.PushStyleColor(ImGuiCol.Button, EditorPalette.logToggle);
            NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, EditorPalette.logToggleHovered);
            NativeImGui.PushStyleColor(ImGuiCol.ButtonActive, EditorPalette.logToggleActive);
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
    #endregion

}
