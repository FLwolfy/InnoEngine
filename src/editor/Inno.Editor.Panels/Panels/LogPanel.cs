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
    private const bool C_SAFE_RENDER = true;
    private const string C_ELLIPSIS = "...";
    private const string C_ICON_BUG = ImGuiIcon.Bug;
    private const string C_ICON_INFO = ImGuiIcon.CircleInfo;
    private const string C_ICON_WARN = ImGuiIcon.TriangleExclamation;
    private const string C_ICON_ERROR = ImGuiIcon.CircleXmark;
    private const string C_ICON_FATAL = ImGuiIcon.SkullCrossbones;
    private const string C_ICON_DEFAULT = ImGuiIcon.FileLines;

    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();
    private readonly float[] m_levelTokenW = new float[Enum.GetValues<LogLevel>().Length];

    private bool m_collapse = true;
    private bool m_levelTokenWValid;
    private float m_levelTokenWFontSize = -1f;

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
        bool oldCollapse = m_collapse;

        NativeImGui.BeginChild("LogChild", Vector2.Zero);
        DrawToolbar(context);
        NativeImGui.Separator();
        DrawLogRegion(context, oldCollapse);
        NativeImGui.EndChild();
    }

    private void DrawToolbar(EditorContext context)
    {
        _ = NativeImGui.Checkbox("Collapse", ref m_collapse);
        NativeImGui.SameLine();
        DrawFilterCombo();
        NativeImGui.SameLine();
        if (NativeImGui.Button("Clear"))
        {
            context.logs.Clear();
        }
    }

    private void DrawFilterCombo()
    {
        if (!NativeImGui.BeginCombo("##LogFilter", "Filter", ImGuiComboFlags.WidthFitPreview))
        {
            return;
        }

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
        }

        NativeImGui.EndCombo();
    }

    private void DrawLogRegion(EditorContext context, bool oldCollapse)
    {
        NativeImGui.BeginChild("LogRegion", Vector2.Zero);

        bool scrollAtBottom = NativeImGui.GetScrollY() >= NativeImGui.GetScrollMaxY() - 1f;
        LogEntry[] entries = context.logs.Snapshot();
        DrawEntries(entries, oldCollapse);

        if (scrollAtBottom)
        {
            NativeImGui.SetScrollHereY(1f);
        }

        NativeImGui.EndChild();
    }

    private void DrawEntries(LogEntry[] entries, bool oldCollapse)
    {
        if (entries.Length == 0)
        {
            NativeImGui.BeginDisabled(true);
            NativeImGui.TextUnformatted("No logs yet.");
            NativeImGui.EndDisabled();
            return;
        }

        LogEntry? last = null;
        int count = 0;
        int visibleCount = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            LogEntry entry = entries[i];
            if (!m_filterLevels.Contains(entry.level))
            {
                continue;
            }

            if (m_collapse && last is { } prevCollapsed && entry.level == prevCollapsed.level && entry.message == prevCollapsed.message)
            {
                last = entry;
                count++;
                continue;
            }

            if (last is { } prev)
            {
                DrawLogEntry(prev, count, oldCollapse);
                visibleCount++;
            }

            last = entry;
            count = 1;
        }

        if (last is { } tail)
        {
            DrawLogEntry(tail, count, oldCollapse);
            visibleCount++;
        }

        if (visibleCount == 0)
        {
            NativeImGui.BeginDisabled(true);
            NativeImGui.TextUnformatted("All logs are filtered out.");
            NativeImGui.EndDisabled();
        }
    }

    private void DrawLogEntry(LogEntry entry, int repeatCount, bool oldCollapse)
    {
        if (C_SAFE_RENDER)
        {
            DrawLogEntrySafe(entry, repeatCount);
            return;
        }

        NativeImGui.PushID(entry.time.Ticks.GetHashCode());

        ImGuiStylePtr style = NativeImGui.GetStyle();
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        ImGuiStoragePtr storage = NativeImGui.GetStateStorage();

        (Vector4 levelColor, string levelIcon) = GetLevelVisual(entry.level);

        uint openId = NativeImGui.GetID("##LogOpen");
        bool open = storage.GetBool(openId, false);
        if (oldCollapse && !m_collapse)
        {
            open = false;
            storage.SetBool(openId, false);
        }

        float padX = style.FramePadding.X;
        const float C_PAD_Y = 2f;

        float arrowSize = NativeImGui.GetFontSize() * 0.50f;
        float gap = style.ItemSpacing.X;

        Vector2 headerMin = NativeImGui.GetCursorScreenPos();
        float fullW = NativeImGui.GetContentRegionAvail().X;
        float lineH = NativeImGui.GetFontSize();
        float wrapStartX = headerMin.X + padX + arrowSize + gap;

        string repeatText = repeatCount > 1 ? $"(x{repeatCount})" : string.Empty;
        float repeatW = repeatCount > 1 ? NativeImGui.CalcTextSize(repeatText).X : 0f;

        float right = (headerMin.X + fullW) - style.FramePadding.X - (repeatW > 0 ? (repeatW + style.ItemSpacing.X) : 0f);
        float msgStartX = wrapStartX + GetLevelTokenWidth(entry.level) + style.ItemSpacing.X;
        float msgAvail = MathF.Max(1f, right - msgStartX);
        if (!float.IsFinite(msgStartX))
        {
            msgStartX = wrapStartX + 80f;
        }
        if (!float.IsFinite(msgAvail) || msgAvail < 1f)
        {
            msgAvail = MathF.Max(1f, fullW * 0.5f);
        }

        string msg = entry.message;
        bool hasNewline = msg.IndexOfAny(['\n', '\r']) >= 0;
        bool willWrap = open && !hasNewline && NativeImGui.CalcTextSize(msg).X > msgAvail;
        float headerContentH = !open
            ? lineH
            : hasNewline
                ? MathF.Max(lineH, NativeImGui.CalcTextSize(msg, false, msgAvail).Y)
                : willWrap
                    ? MathF.Max(lineH, NativeImGui.CalcTextSize(msg, false, msgAvail).Y)
                    : lineH;

        float headerH = headerContentH + C_PAD_Y * 2f;
        Vector2 headerMax = new(headerMin.X + fullW, headerMin.Y + headerH);

        _ = NativeImGui.InvisibleButton("##LogHeaderBtn", new Vector2(fullW, headerH));
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            open = !open;
            storage.SetBool(openId, open);
        }

        drawList.AddRectFilled(
            headerMin,
            headerMax,
            LerpU32(NativeImGui.GetColorU32(ImGuiCol.Header), NativeImGui.GetColorU32(ImGuiCol.WindowBg), 0.15f),
            style.FrameRounding);

        DrawArrow(
            drawList,
            new Vector2(headerMin.X + padX + arrowSize * 0.5f, headerMin.Y + C_PAD_Y + lineH * 0.5f),
            arrowSize,
            open,
            NativeImGui.GetColorU32(ImGuiCol.Text));

        DrawHeaderText(
            entry,
            levelColor,
            levelIcon,
            headerMin,
            headerMax,
            wrapStartX,
            C_PAD_Y,
            repeatText,
            repeatW,
            msgStartX,
            msgAvail,
            open,
            willWrap,
            hasNewline);

        if (open)
        {
            DrawDetailsBlock(entry, headerMin, headerMax, style, drawList);
        }
        else
        {
            NativeImGui.Dummy(Vector2.Zero);
        }

        NativeImGui.PopID();
    }

    private static void DrawLogEntrySafe(LogEntry entry, int repeatCount)
    {
        (Vector4 levelColor, string levelIcon) = GetLevelVisual(entry.level);
        string messagePreview = entry.message;
        bool hasNewline = messagePreview.IndexOfAny(['\n', '\r']) >= 0;
        if (hasNewline)
        {
            messagePreview = GetFirstLine(messagePreview) + C_ELLIPSIS;
        }

        string repeatText = repeatCount > 1 ? $" (x{repeatCount})" : string.Empty;
        string header = $"{levelIcon} [{entry.level}] {messagePreview}{repeatText}";

        NativeImGui.PushID(entry.time.Ticks.GetHashCode());
        NativeImGui.PushStyleColor(ImGuiCol.Text, levelColor);
        bool open = NativeImGui.TreeNodeEx(
            header,
            ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding);
        NativeImGui.PopStyleColor();

        if (!open)
        {
            NativeImGui.PopID();
            return;
        }

        NativeImGui.BeginDisabled(true);
        NativeImGui.TextUnformatted($"Time: {entry.time:HH:mm:ss}");
        NativeImGui.TextUnformatted($"Source: {entry.source}");
        NativeImGui.TextUnformatted($"File: {entry.file}");
        NativeImGui.TextUnformatted($"Line: {entry.line}");
        NativeImGui.EndDisabled();
        NativeImGui.TreePop();
        NativeImGui.PopID();
    }

    private void DrawHeaderText(
        LogEntry entry,
        Vector4 levelColor,
        string levelIcon,
        Vector2 headerMin,
        Vector2 headerMax,
        float wrapStartX,
        float padY,
        string repeatText,
        float repeatW,
        float msgStartX,
        float msgAvail,
        bool open,
        bool willWrap,
        bool hasNewline)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();

        NativeImGui.SetCursorScreenPos(new Vector2(wrapStartX, headerMin.Y + padY));

        NativeImGui.PushStyleColor(ImGuiCol.Text, levelColor);
        NativeImGui.TextUnformatted(levelIcon);
        NativeImGui.SameLine();
        NativeImGui.TextUnformatted($"[{entry.level}]");
        NativeImGui.PopStyleColor();

        NativeImGui.SetCursorScreenPos(new Vector2(msgStartX, headerMin.Y + padY));

        string msg = entry.message;
        if (!open)
        {
            if (!hasNewline)
            {
                DrawSingleLineEllipsis(msg, msgAvail);
            }
            else
            {
                string firstLine = GetFirstLine(msg);
                DrawSingleLineEllipsisForcedSuffix(firstLine, msgAvail);
            }
        }
        else
        {
            if (!hasNewline && !willWrap)
            {
                NativeImGui.TextUnformatted(msg);
            }
            else
            {
                NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + msgAvail);
                NativeImGui.TextUnformatted(msg);
                NativeImGui.PopTextWrapPos();
            }
        }

        if (repeatW > 0f)
        {
            drawList.AddText(
                new Vector2(headerMax.X - style.FramePadding.X - repeatW, headerMin.Y + padY),
                NativeImGui.GetColorU32(ImGuiCol.TextDisabled),
                repeatText);
        }
    }

    private static void DrawDetailsBlock(LogEntry entry, Vector2 headerMin, Vector2 headerMax, ImGuiStylePtr style, ImDrawListPtr drawList)
    {
        NativeImGui.SetCursorScreenPos(new Vector2(headerMin.X, headerMax.Y + style.ItemSpacing.Y));

        Vector2 blockMin = NativeImGui.GetCursorScreenPos();
        float blockW = NativeImGui.GetContentRegionAvail().X;

        string details =
            $"Time: {entry.time:HH:mm:ss}\n" +
            $"Source: {entry.source}\n" +
            $"File: {entry.file}\n" +
            $"Line: {entry.line}";

        float padX = style.FramePadding.X;
        float padY = style.FramePadding.Y;

        Vector2 size = NativeImGui.CalcTextSize(details, false, blockW - padX * 2f);
        float blockH = size.Y + padY * 2f;

        drawList.AddRectFilled(
            blockMin,
            new Vector2(blockMin.X + blockW, blockMin.Y + blockH),
            LerpU32(NativeImGui.GetColorU32(ImGuiCol.Header), NativeImGui.GetColorU32(ImGuiCol.WindowBg), 0.75f),
            style.FrameRounding);

        NativeImGui.SetCursorScreenPos(new Vector2(blockMin.X + padX, blockMin.Y + padY));
        NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + MathF.Max(1f, blockW - padX * 2f));
        NativeImGui.TextUnformatted(details);
        NativeImGui.PopTextWrapPos();

        NativeImGui.SetCursorScreenPos(new Vector2(blockMin.X, blockMin.Y + blockH));
        NativeImGui.Dummy(Vector2.Zero);
    }

    private float GetLevelTokenWidth(LogLevel level)
    {
        float fontSize = NativeImGui.GetFontSize();
        if (!m_levelTokenWValid || MathF.Abs(m_levelTokenWFontSize - fontSize) > 0.01f)
        {
            m_levelTokenWFontSize = fontSize;
            for (int i = 0; i < m_levels.Length; i++)
            {
                LogLevel lv = m_levels[i];
                float w = 0f;
                // Do not use icon glyph for width calculation here.
                // Some font/encoding combinations can return unstable metrics for private-use glyphs.
                float levelWidth = NativeImGui.CalcTextSize($"[{lv}]").X;
                float iconWidthApprox = MathF.Max(8f, fontSize * 0.75f);
                w += iconWidthApprox;
                w += NativeImGui.GetStyle().ItemSpacing.X;
                w += levelWidth;

                if (!float.IsFinite(w) || w <= 0f || w > 4096f)
                {
                    w = MathF.Max(24f, fontSize * 3f);
                }

                m_levelTokenW[(int)lv] = w;
            }

            m_levelTokenWValid = true;
        }

        float width = m_levelTokenW[(int)level];
        if (!float.IsFinite(width) || width <= 0f || width > 4096f)
        {
            return MathF.Max(24f, NativeImGui.GetFontSize() * 3f);
        }

        return width;
    }

    private static (Vector4 color, string icon) GetLevelVisual(LogLevel level) => level switch
    {
        LogLevel.Debug => (new Vector4(0.80f, 0.90f, 0.85f, 1f), C_ICON_BUG),
        LogLevel.Info => (new Vector4(0.20f, 1f, 0.20f, 1f), C_ICON_INFO),
        LogLevel.Warn => (new Vector4(1f, 1f, 0.20f, 1f), C_ICON_WARN),
        LogLevel.Error => (new Vector4(1f, 0.20f, 0.20f, 1f), C_ICON_ERROR),
        LogLevel.Fatal => (new Vector4(1f, 0.20f, 1f, 1f), C_ICON_FATAL),
        _ => (Vector4.One, C_ICON_DEFAULT)
    };

    private static uint LerpU32(uint a, uint b, float t)
    {
        Vector4 va = NativeImGui.ColorConvertU32ToFloat4(a);
        Vector4 vb = NativeImGui.ColorConvertU32ToFloat4(b);
        Vector4 v = LerpColor(va, vb, t);
        return NativeImGui.ColorConvertFloat4ToU32(v);
    }

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);
    }

    private static void DrawArrow(ImDrawListPtr drawList, Vector2 center, float size, bool open, uint color)
    {
        if (!open)
        {
            size *= 0.75f;
        }

        float h = size;
        float w = size * 0.9f;

        if (!open)
        {
            Vector2 p1 = new(center.X - w * 0.35f, center.Y - h * 0.5f);
            Vector2 p2 = new(center.X - w * 0.35f, center.Y + h * 0.5f);
            Vector2 p3 = new(center.X + w * 0.55f, center.Y);
            drawList.AddTriangleFilled(p1, p2, p3, color);
            return;
        }

        Vector2 q1 = new(center.X - w * 0.5f, center.Y - h * 0.25f);
        Vector2 q2 = new(center.X + w * 0.5f, center.Y - h * 0.25f);
        Vector2 q3 = new(center.X, center.Y + h * 0.55f);
        drawList.AddTriangleFilled(q1, q2, q3, color);
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

    private static void DrawSingleLineEllipsis(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 1f)
        {
            NativeImGui.TextUnformatted(string.Empty);
            return;
        }

        if (NativeImGui.CalcTextSize(text).X <= maxWidth)
        {
            NativeImGui.TextUnformatted(text);
            return;
        }

        float ellipsisW = NativeImGui.CalcTextSize(C_ELLIPSIS).X;
        if (ellipsisW >= maxWidth)
        {
            NativeImGui.TextUnformatted(C_ELLIPSIS);
            return;
        }

        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            float w = NativeImGui.CalcTextSize(text[..mid]).X;
            if (w + ellipsisW <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        int cut = lo;
        while (cut > 0 && char.IsWhiteSpace(text[cut - 1]))
        {
            cut--;
        }

        NativeImGui.TextUnformatted(cut <= 0 ? C_ELLIPSIS : text[..cut] + C_ELLIPSIS);
    }

    private static void DrawSingleLineEllipsisForcedSuffix(string text, float maxWidth)
    {
        if (maxWidth <= 1f)
        {
            NativeImGui.TextUnformatted(string.Empty);
            return;
        }

        float suffixW = NativeImGui.CalcTextSize(C_ELLIPSIS).X;
        if (suffixW >= maxWidth)
        {
            NativeImGui.TextUnformatted(C_ELLIPSIS);
            return;
        }

        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            float w = NativeImGui.CalcTextSize(text[..mid]).X;
            if (w + suffixW <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        int cut = lo;
        while (cut > 0 && char.IsWhiteSpace(text[cut - 1]))
        {
            cut--;
        }

        string body = cut <= 0 ? string.Empty : text[..cut];
        NativeImGui.TextUnformatted(body + C_ELLIPSIS);
    }
}
