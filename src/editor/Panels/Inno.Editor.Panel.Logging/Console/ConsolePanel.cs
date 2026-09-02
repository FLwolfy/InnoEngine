using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Diagnostics;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Displays append-only logs and current diagnostics in the unified editor Console.
/// </summary>
[EditorPanel("diagnostics.console", "Console", order: 400, menuPath: "Diagnostics")]
internal sealed class ConsolePanel : EditorPanel
{
    #region State
    private readonly ConsolePanelContent m_content = new();
    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();
    private readonly HashSet<string> m_openEntries = new(StringComparer.Ordinal);

    private bool m_collapse = true;
    private long m_lastRevision = -1;
    private bool m_requestScrollToBottom = true;
    private readonly IEditorConsole m_console;
    private readonly EditorInteractions m_interactions;
    #endregion

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    /// <param name="console">
    /// The editor Console contract that owns snapshots and explicit retention operations.
    /// </param>
    /// <param name="interactions">
    /// The editor interaction entry point used by contextual entry commands.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="console"/> or <paramref name="interactions"/> is <see langword="null"/>.
    /// </exception>
    internal ConsolePanel(IEditorConsole console, EditorInteractions interactions)
    {
        m_console = console ?? throw new ArgumentNullException(nameof(console));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <summary>
    /// Captures the persistent Console layout preferences without retaining log content.
    /// </summary>
    /// <param name="state">
    /// The project-level panel state that receives stable presentation values.
    /// </param>
    protected override void Capture(EditorState state)
    {
        state.Set("collapse", m_collapse);
    }

    /// <summary>
    /// Restores persistent Console layout preferences without restoring historical log content.
    /// </summary>
    /// <param name="state">
    /// The project-level panel state containing stable presentation values.
    /// </param>
    protected override void Restore(EditorState state)
    {
        m_collapse = state.Get("collapse", true);
    }

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        NativeImGui.BeginChild("ConsoleChild", Vector2.Zero);
        DrawToolbar();
        NativeImGui.Separator();
        DrawConsoleRegion();
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
            m_console.Clear();
            m_openEntries.Clear();
            m_requestScrollToBottom = true;
        }
    }

    private bool DrawFilterCombo()
    {
        if (!EditorWidget.BeginBoundedCombo(
                "##ConsoleFilter",
                "Filter",
                ImGuiComboFlags.WidthFitPreview))
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

    #region Console Region
    private void DrawConsoleRegion()
    {
        NativeImGui.BeginChild("ConsoleRegion", Vector2.Zero);

        EditorConsoleSnapshot snapshot = m_console.Capture();
        RemoveStaleOpenEntries(snapshot);
        bool hasNewEntries = m_lastRevision >= 0 && snapshot.revision != m_lastRevision;
        m_lastRevision = snapshot.revision;

        bool scrollAtBottom = NativeImGui.GetScrollY() >=
                              NativeImGui.GetScrollMaxY() - EditorWidget.style.logAutoScrollTolerance;
        bool shouldScrollToBottom = m_requestScrollToBottom || (hasNewEntries && scrollAtBottom);
        DrawEntries(snapshot);

        if (shouldScrollToBottom)
        {
            NativeImGui.SetScrollHereY(1f);
            m_requestScrollToBottom = false;
        }

        NativeImGui.EndChild();
    }
    #endregion

    #region Entries
    private void DrawEntries(EditorConsoleSnapshot snapshot)
    {
        if (snapshot.occurrences.Count == 0)
        {
            m_content.DrawDisabledHint("No logs or diagnostics yet.");
            return;
        }

        int visibleCount = 0;
        if (m_collapse)
        {
            for (int i = 0; i < snapshot.groups.Count; i++)
            {
                EditorConsoleGroup group = snapshot.groups[i];
                if (!m_filterLevels.Contains(group.latest.level))
                    continue;
                visibleCount++;
                DrawConsoleEntry(
                    group.latest,
                    group.occurrences,
                    group.count,
                    $"group/{group.identity}");
            }
        }
        else
        {
            for (int i = 0; i < snapshot.occurrences.Count; i++)
            {
                EditorConsoleOccurrence occurrence = snapshot.occurrences[i];
                if (!m_filterLevels.Contains(occurrence.level))
                    continue;
                visibleCount++;
                DrawConsoleEntry(
                    occurrence,
                    [occurrence],
                    1,
                    $"occurrence/{occurrence.sequence}");
            }
        }

        if (visibleCount == 0)
            m_content.DrawDisabledHint("All logs are filtered out.");
    }
    #endregion

    #region Entry Card
    private void DrawConsoleEntry(
        EditorConsoleOccurrence entry,
        IReadOnlyList<EditorConsoleOccurrence> occurrences,
        int repeatCount,
        string identity)
    {
        (Vector4 levelColor, string levelIcon) = m_content.GetLevelVisual(entry.level);

        ConsoleEntryImGuiIdentity.Push(identity);
        bool open = m_openEntries.Contains(identity);
        if (!open)
        {
            Vector4 collapsedCardBg = m_content.GetCollapsedBgColor();
            Vector4 collapsedCardBorder = m_content.GetCollapsedBorderColor();
            NativeImGui.PushStyleColor(ImGuiCol.FrameBg, collapsedCardBg);
            NativeImGui.PushStyleColor(ImGuiCol.Border, collapsedCardBorder);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, NativeImGui.GetStyle().FrameRounding);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, EditorWidget.style.borderSize);
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.framePadding);

            ImGuiChildFlags collapsedChildFlags = ImGuiChildFlags.FrameStyle;
            ImGuiWindowFlags collapsedChildWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings;
            float collapsedHeight = NativeImGui.GetFrameHeight() + NativeImGui.GetStyle().FramePadding.Y * 2f;
            if (NativeImGui.BeginChild("##ConsoleEntryCard", new Vector2(0f, collapsedHeight), collapsedChildFlags, collapsedChildWindowFlags))
            {
                DrawHeader(entry, repeatCount, levelColor, levelIcon, false, collapsedCardBg, out bool toggled);
                if (toggled)
                    m_openEntries.Add(identity);
            }

            NativeImGui.EndChild();
            DrawEntryContextMenu(entry, repeatCount);
            NativeImGui.PopStyleVar(3);
            NativeImGui.PopStyleColor(2);
            ConsoleEntryImGuiIdentity.Pop();
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
        if (NativeImGui.BeginChild("##ConsoleEntryCard", new Vector2(0f, 0f), childFlags, childWindowFlags))
        {
            DrawHeader(entry, repeatCount, levelColor, levelIcon, true, cardBg, out bool toggled);
            if (toggled)
                m_openEntries.Remove(identity);

            NativeImGui.PushStyleColor(
                ImGuiCol.Separator,
                EditorPalette.GetLogSeparator(cardBg));
            try
            {
                NativeImGui.Separator();
                for (int i = 0; i < occurrences.Count; i++)
                {
                    if (i > 0)
                        NativeImGui.Separator();
                    NativeImGui.PushID(unchecked((int)occurrences[i].sequence));
                    DrawDetail(occurrences[i]);
                    NativeImGui.PopID();
                }
            }
            finally
            {
                NativeImGui.PopStyleColor();
            }
        }

        NativeImGui.EndChild();
        DrawEntryContextMenu(entry, repeatCount);
        NativeImGui.PopStyleVar(3);
        NativeImGui.PopStyleColor(2);
        ConsoleEntryImGuiIdentity.Pop();
    }
    #endregion

    #region Entry Header / Detail
    private void DrawHeader(
        EditorConsoleOccurrence entry,
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

    private static void DrawDetail(EditorConsoleOccurrence entry)
    {
        string timeText = entry.time.ToString("HH:mm:ss");
        string sourceText = entry.source;
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

        if (NativeImGui.BeginTable("##ConsoleEntryDetails", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
        {
            NativeImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
            NativeImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize, 1f);
            DrawDetailFieldRow("Kind:", entry.kind.ToString());
            DrawDetailFieldRow("File:", fileWithLineText);
            DrawDetailFieldRow("Source:", sourceText);
            if (entry.sessionId.isAssigned)
                DrawDetailFieldRow("Session:", entry.sessionId.ToString());
            DrawDetailFieldRow("Time:", timeText);
            NativeImGui.EndTable();
        }

        if (!string.IsNullOrWhiteSpace(entry.stackTrace))
        {
            NativeImGui.Separator();
            NativeImGui.PushTextWrapPos(0f);
            NativeImGui.TextUnformatted(entry.stackTrace);
            NativeImGui.PopTextWrapPos();
        }
    }

    private void DrawEntryContextMenu(EditorConsoleOccurrence entry, int repeatCount)
    {
        _ = EditorMenuRenderer.ContextMenu(
            "##ConsoleEntryContextMenu",
            m_interactions.For(
                LoggingInteractionIds.C_ENTRY_AREA,
                new ConsoleEntryCopyTarget(entry, repeatCount)));
    }

    private void RemoveStaleOpenEntries(EditorConsoleSnapshot snapshot)
    {
        if (m_openEntries.Count == 0)
            return;
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.groups.Count; i++)
            activeIds.Add($"group/{snapshot.groups[i].identity}");
        for (int i = 0; i < snapshot.occurrences.Count; i++)
            activeIds.Add($"occurrence/{snapshot.occurrences[i].sequence}");
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
