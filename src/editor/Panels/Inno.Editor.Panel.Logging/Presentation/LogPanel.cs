using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Displays append-only logs and current diagnostics with filtering, collapse, details, and contextual copy actions.
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
    private long m_lastLogVersion = -1;
    private long m_lastDiagnosticVersion = -1;
    private bool m_requestScrollToBottom = true;
    private bool m_lastRenderedCollapse = true;
    private readonly LoggingModule m_logging;
    private readonly EditorInteractions m_interactions;
    #endregion

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="logging">The automatically discovered module that connects both Console data sources.</param>
    /// <param name="interactions">The editor interaction entry point used by contextual entry commands.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logging"/> or <paramref name="interactions"/> is <see langword="null"/>.
    /// </exception>
    public LogPanel(LoggingModule logging, EditorInteractions interactions)
    {
        m_logging = logging ?? throw new ArgumentNullException(nameof(logging));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        NativeImGui.BeginChild("LogChild", Vector2.Zero);
        DrawToolbar();
        NativeImGui.Separator();
        DrawLogRegion();
        NativeImGui.EndChild();
    }
    #endregion

    #region Toolbar
    private void DrawToolbar()
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
            m_logging.logs.Clear();
            m_logging.diagnostics.Clear();
            m_openEntries.Clear();
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
    private void DrawLogRegion()
    {
        NativeImGui.BeginChild("LogRegion", Vector2.Zero);

        BufferedLogEntry[] logs = m_logging.logs.SnapshotBuffered(out long logVersion);
        EditorDiagnosticEntry[] diagnostics = m_logging.diagnostics.Snapshot(out long diagnosticVersion);
        EditorConsoleEntry[] entries = m_content.Combine(logs, diagnostics);
        RemoveStaleOpenEntries(entries);
        bool hasNewEntries =
            m_lastLogVersion >= 0 && logVersion != m_lastLogVersion ||
            m_lastDiagnosticVersion >= 0 && diagnosticVersion != m_lastDiagnosticVersion;
        m_lastLogVersion = logVersion;
        m_lastDiagnosticVersion = diagnosticVersion;

        bool scrollAtBottom = NativeImGui.GetScrollY() >=
                              NativeImGui.GetScrollMaxY() - EditorWidget.style.logAutoScrollTolerance;
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
    private void DrawEntries(EditorConsoleEntry[] entries)
    {
        if (entries.Length == 0)
        {
            m_content.DrawDisabledHint("No logs or diagnostics yet.");
            return;
        }

        List<EditorConsoleEntry> visibleEntries = m_content.CollectVisibleEntries(entries, m_filterLevels);

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
                    long entryId = visibleEntries[i].id;
                    DrawLogEntry(visibleEntries[i], 1, [entryId], collapseView: false);
                }
            }

            start = end + 1;
        }
    }
    #endregion

    #region Entry Card
    private void DrawLogEntry(
        EditorConsoleEntry entry,
        int repeatCount,
        IReadOnlyList<long> runEntryIds,
        bool collapseView)
    {
        (Vector4 levelColor, string levelIcon) = m_content.GetLevelVisual(entry.level);

        long entryId = entry.id;
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
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, EditorWidget.style.borderSize);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.framePadding);

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
            DrawEntryContextMenu(entry, repeatCount);
            NativeImGui.PopStyleVar(3);
            NativeImGui.PopStyleColor(2);
            NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.logCollapsedSpacing));
            NativeImGui.PopID();
            return;
        }

        Vector4 cardBg = m_content.GetExpandedBgColor(levelColor);
        Vector4 cardBorder = EditorPalette.GetLogExpandedBorder(levelColor);
        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, cardBg);
        NativeImGui.PushStyleColor(ImGuiCol.Border, cardBorder);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, EditorWidget.style.borderSize);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.framePadding);

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
        DrawEntryContextMenu(entry, repeatCount);
        NativeImGui.PopStyleVar(3);
        NativeImGui.PopStyleColor(2);
        NativeImGui.Dummy(new Vector2(0f, EditorWidget.style.logExpandedSpacing));
        NativeImGui.PopID();
    }
    #endregion

    #region Entry Header / Detail
    private void DrawHeader(
        EditorConsoleEntry entry,
        int repeatCount,
        Vector4 levelColor,
        string levelIcon,
        bool isOpen,
        Vector4 headerBgColor,
        out bool toggled)
    {
        toggled = false;
        ImGuiStylePtr style = NativeImGui.GetStyle();
        
        string content = isOpen ? entry.displayMessage : m_content.GetFirstLine(entry.displayMessage);
        string repeatText = repeatCount > 1 ? $" (x{repeatCount})" : string.Empty;
        string prefix = $"{levelIcon} [{entry.level}] ";
        string toggleText = isOpen ? "▼" : "▶";
        
        float toggleW = NativeImGui.CalcTextSize(toggleText).X + EditorWidget.style.logDisclosurePadding.X * 2f;
        float prefixW = NativeImGui.CalcTextSize(prefix).X + toggleW + style.ItemInnerSpacing.X;
        float suffixW = NativeImGui.CalcTextSize(repeatText).X;
        float suffixColW = MathF.Max(1f, suffixW);
        float tableOuterW = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float contentW = MathF.Max(1f, tableOuterW - prefixW - suffixColW);
        
        if (!isOpen)
        {
            content = m_content.FitTextWithEllipsis(m_content.GetFirstLine(entry.displayMessage), contentW);
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
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.logDisclosurePadding);
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

    private static void DrawDetail(EditorConsoleEntry entry)
    {
        string timeText = entry.time.ToString("HH:mm:ss");
        string sourceText = entry.source.ToString();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            sourceText = "-";
        }

        string fileText = string.IsNullOrWhiteSpace(entry.file) ? "-" : entry.file;
        string fileWithLineText = entry.line > 0
            ? entry.column > 0
                ? $"{fileText}:{entry.line}:{entry.column}"
                : $"{fileText}:{entry.line}"
            : fileText;

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

    private void DrawEntryContextMenu(EditorConsoleEntry entry, int repeatCount)
    {
        _ = EditorMenuRenderer.ContextMenu(
            "##LogEntryContextMenu",
            m_interactions.For(
                LogPanelAreas.Entry,
                new LogEntryCopyTarget(entry, repeatCount)));
    }

    private void RemoveStaleOpenEntries(IReadOnlyList<EditorConsoleEntry> entries)
    {
        if (m_openEntries.Count == 0)
            return;
        var activeIds = new HashSet<long>(entries.Select(static entry => entry.id));
        m_openEntries.RemoveWhere(id => !activeIds.Contains(id));
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
