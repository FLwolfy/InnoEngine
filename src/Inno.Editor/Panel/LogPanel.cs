using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;

using ImGuiNET;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public sealed class LogPanel : EditorPanel, ILogSink
{
    private const string C_ELLIPSE = "...";
    private const int C_MAX_LOG_ENTRIES = 100;

    public override string title => "Log";

    private readonly Queue<LogEntry> m_pendingEntries = new();
    private readonly List<LogEntry> m_entries = new();
    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();

    private bool m_collapse = true;

    // Cache: width of (icon + "[Level]") token per level for current font size
    private readonly float[] m_levelTokenW = new float[Enum.GetValues<LogLevel>().Length];
    private float m_levelTokenWFontSize = -1f;
    private bool m_levelTokenWValid;

    public LogPanel() => LogManager.RegisterSink(this);

    public void Receive(LogEntry entry)
    {
        lock (m_pendingEntries) m_pendingEntries.Enqueue(entry);
    }

    internal override void OnGUI()
    {
        DrainPending();

        ImGuiNet.BeginChild("LogChild", new Vector2(0, 0));

        var oldCollapse = m_collapse;

        ImGuiNet.Checkbox("Collapse", ref m_collapse);
        ImGuiNet.SameLine();

        DrawFilterCombo();

        ImGuiNet.SameLine();
        if (ImGuiNet.Button("Clear")) m_entries.Clear();

        ImGuiNet.Separator();

        ImGuiNet.BeginChild("LogRegion", new Vector2(0, 0));
        var scrollAtBottom = ImGuiNet.GetScrollY() >= ImGuiNet.GetScrollMaxY() - 1f;

        CollapseIter(oldCollapse);

        if (scrollAtBottom) ImGuiNet.SetScrollHereY(1f);

        ImGuiNet.EndChild();
        ImGuiNet.EndChild();
    }

    private void DrainPending()
    {
        lock (m_pendingEntries)
        {
            while (m_pendingEntries.Count > 0) m_entries.Add(m_pendingEntries.Dequeue());
            while (m_entries.Count > C_MAX_LOG_ENTRIES) m_entries.RemoveAt(0);
        }
    }

    private void DrawFilterCombo()
    {
        if (!ImGuiNet.BeginCombo("##LogFilter", "Filter", ImGuiComboFlags.WidthFitPreview)) return;

        foreach (var level in m_levels)
        {
            var selected = m_filterLevels.Contains(level);
            if (!ImGuiNet.Checkbox(level.ToString(), ref selected)) continue;

            if (selected) m_filterLevels.Add(level);
            else m_filterLevels.Remove(level);
        }

        ImGuiNet.EndCombo();
    }

    private void CollapseIter(bool oldCollapse)
    {
        LogEntry? last = null;
        var count = 0;

        foreach (var e in m_entries)
        {
            if (!m_filterLevels.Contains(e.level)) continue;

            if (m_collapse && last is { } le && e.level == le.level && e.message == le.message)
            {
                last = e;
                count++;
                continue;
            }

            if (last is { } prev)
            {
                if (oldCollapse && !m_collapse) ImGuiNet.SetNextItemOpen(false);
                DrawLogEntry(prev, count);
            }

            last = e;
            count = 1;
        }

        if (last is { } tail) DrawLogEntry(tail, count);
    }

    private void DrawLogEntry(LogEntry entry, int repeatCount)
    {
        ImGuiNet.PushID(entry.time.GetHashCode());

        var style = ImGuiNet.GetStyle();
        var dl = ImGuiNet.GetWindowDrawList();
        var storage = ImGuiNet.GetStateStorage();

        var (levelColor, levelIcon) = GetLevelVisual(entry.level);

        var openId = ImGuiNet.GetID("##LogOpen");
        var open = storage.GetBool(openId, false);

        var padX = style.FramePadding.X;
        const float c_padY = 2f;

        var arrowSize = ImGuiNet.GetFontSize() * 0.50f;
        var gap = style.ItemSpacing.X;

        var headerMin = ImGuiNet.GetCursorScreenPos();
        var fullW = ImGuiNet.GetContentRegionAvail().X;
        var lineH = ImGuiNet.GetFontSize();
        var wrapStartX = headerMin.X + padX + arrowSize + gap;

        var repeatText = repeatCount > 1 ? $"(x{repeatCount})" : string.Empty;
        var repeatW = repeatCount > 1 ? ImGuiNet.CalcTextSize(repeatText).X : 0f;

        // ---- Force header height based on "will the message wrap?" (no reliance on TextWrapped internal wrap) ----
        var right = (headerMin.X + fullW) - style.FramePadding.X
                  - (repeatW > 0 ? (repeatW + style.ItemSpacing.X) : 0f);

        // Message start X is forced to this estimate, so header height and drawn text are consistent.
        var msgStartX = wrapStartX + GetLevelTokenWidth(entry.level) + style.ItemSpacing.X;
        var msgAvail = MathF.Max(1f, right - msgStartX);

        var msg = entry.message;
        var hasNewline = msg.IndexOfAny(['\n', '\r']) >= 0;
        var willWrap = open && !hasNewline && ImGuiNet.CalcTextSize(msg).X > msgAvail;
        var headerContentH = !open
            ? lineH
            : hasNewline
                ? MathF.Max(lineH, ImGuiNet.CalcTextSize(msg, false, msgAvail).Y)
                : willWrap
                    ? MathF.Max(lineH, ImGuiNet.CalcTextSize(msg, false, msgAvail).Y)
                    : lineH;

        var headerH = headerContentH + c_padY * 2f;
        var headerMax = new Vector2(headerMin.X + fullW, headerMin.Y + headerH);

        // clickable header
        ImGuiNet.InvisibleButton("##LogHeaderBtn", new Vector2(fullW, headerH));
        if (ImGuiNet.IsItemClicked(ImGuiMouseButton.Left))
        {
            open = !open;
            storage.SetBool(openId, open);
            // Re-evaluate willWrap next frame naturally (open changed).
        }

        dl.AddRectFilled(
            headerMin,
            headerMax,
            LerpU32(ImGuiNet.GetColorU32(ImGuiCol.Header), ImGuiNet.GetColorU32(ImGuiCol.WindowBg), 0.15f),
            style.FrameRounding
        );

        DrawArrow(
            dl,
            new Vector2(headerMin.X + padX + arrowSize * 0.5f, headerMin.Y + c_padY + lineH * 0.5f),
            arrowSize,
            open,
            ImGuiNet.GetColorU32(ImGuiCol.Text)
        );

        DrawHeaderText(
            entry,
            levelColor, 
            levelIcon, 
            headerMin, 
            headerMax,
            wrapStartX,
            c_padY, 
            repeatText, 
            repeatW, 
            msgStartX, 
            msgAvail, 
            open, 
            willWrap,
            hasNewline);

        if (open) DrawDetailsBlock(entry, headerMin, headerMax, style, dl);
        else ImGuiNet.Dummy(new Vector2(0, 0));

        ImGuiNet.PopID();
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
        var style = ImGuiNet.GetStyle();

        ImGuiNet.SetCursorScreenPos(new Vector2(wrapStartX, headerMin.y + padY));
        var baseCursor = ImGuiNet.GetCursorPos();

        ImGuiNet.PushStyleColor(ImGuiCol.Text, levelColor);

        ImGuiHost.UseFont(ImGuiFontStyle.Icon);
        ImGuiNet.SetCursorPosY(baseCursor.Y + style.FramePadding.Y);
        ImGuiNet.TextUnformatted(levelIcon);

        ImGuiNet.SameLine();

        ImGuiHost.UseFont(ImGuiFontStyle.Bold);
        ImGuiNet.SetCursorPosY(baseCursor.Y);
        ImGuiNet.TextUnformatted($"[{entry.level}]");

        ImGuiHost.UseFont(ImGuiFontStyle.Regular);

        ImGuiNet.PopStyleColor();

        // Force message start X to match the measurement used for header height.
        ImGuiNet.SetCursorScreenPos(new Vector2(msgStartX, headerMin.y + padY));
        ImGuiNet.SetCursorPosY(baseCursor.Y);

        var msg = entry.message;

        if (!open)
        {
            if (!hasNewline)
            {
                DrawSingleLineEllipsis(msg, msgAvail);
            }
            else
            {
                var firstLine = GetFirstLine(msg);
                DrawSingleLineEllipsisForcedSuffix(firstLine, msgAvail);
            }
        }
        else
        {
            if (!hasNewline && !willWrap)
            {
                ImGuiNet.TextUnformatted(msg);
            }
            else
            {
                ImGuiNet.PushTextWrapPos(ImGuiNet.GetCursorPosX() + msgAvail);
                ImGuiNet.TextUnformatted(msg);
                ImGuiNet.PopTextWrapPos();
            }
        }


        if (repeatW > 0f)
            ImGuiNet.GetWindowDrawList().AddText(
                new Vector2(headerMax.x - style.FramePadding.X - repeatW, headerMin.y + padY),
                ImGuiNet.GetColorU32(ImGuiCol.TextDisabled),
                repeatText
            );
    }

    private void DrawDetailsBlock(LogEntry entry, Vector2 headerMin, Vector2 headerMax, ImGuiStylePtr style, ImDrawListPtr dl)
    {
        ImGuiNet.SetCursorScreenPos(new Vector2(headerMin.x, headerMax.y + style.ItemSpacing.Y));

        var blockMin = ImGuiNet.GetCursorScreenPos();
        var blockW = ImGuiNet.GetContentRegionAvail().X;

        var details =
            $"Time: {entry.time:HH:mm:ss}\n" +
            $"Source: {entry.source}\n" +
            $"File: {entry.file}\n" +
            $"Line: {entry.line}";

        var padX = style.FramePadding.X;
        var padY = style.FramePadding.Y;

        var size = ImGuiNet.CalcTextSize(details, false, blockW - padX * 2f);
        var blockH = size.Y + padY * 2f;

        dl.AddRectFilled(
            blockMin,
            new Vector2(blockMin.X + blockW, blockMin.Y + blockH),
            LerpU32(ImGuiNet.GetColorU32(ImGuiCol.Header), ImGuiNet.GetColorU32(ImGuiCol.WindowBg), 0.75f),
            style.FrameRounding
        );

        ImGuiNet.SetCursorScreenPos(new Vector2(blockMin.X + padX, blockMin.Y + padY));
        ImGuiNet.PushTextWrapPos(ImGuiNet.GetCursorPosX() + MathF.Max(1f, blockW - padX * 2f));
        ImGuiNet.TextUnformatted(details);
        ImGuiNet.PopTextWrapPos();

        ImGuiNet.SetCursorScreenPos(new Vector2(blockMin.X, blockMin.Y + blockH));
        ImGuiNet.Dummy(new Vector2(0, 0));
    }

    private float GetLevelTokenWidth(LogLevel level)
    {
        var fs = ImGuiNet.GetFontSize();
        if (!m_levelTokenWValid || MathF.Abs(m_levelTokenWFontSize - fs) > 0.01f)
        {
            m_levelTokenWFontSize = fs;

            foreach (var lv in m_levels)
            {
                var icon = GetLevelVisual(lv).icon;

                var w = 0f;

                ImGuiHost.UseFont(ImGuiFontStyle.Icon);
                w += ImGuiNet.CalcTextSize(icon).X;

                ImGuiHost.UseFont(ImGuiFontStyle.Bold);
                w += ImGuiNet.CalcTextSize($"[{lv}]").X;

                ImGuiHost.UseFont(ImGuiFontStyle.Regular);

                m_levelTokenW[(int)lv] = w;
            }

            m_levelTokenWValid = true;
        }

        return m_levelTokenW[(int)level];
    }

    private static (Vector4 col, string icon) GetLevelVisual(LogLevel level) => level switch
    {
        LogLevel.Debug => (new Vector4(0.8f, 0.9f, 0.85f, 1f), ImGuiIcon.Bug),
        LogLevel.Info  => (new Vector4(0.2f, 1f, 0.2f, 1f), ImGuiIcon.CircleInfo),
        LogLevel.Warn  => (new Vector4(1f, 1f, 0.2f, 1f), ImGuiIcon.TriangleExclamation),
        LogLevel.Error => (new Vector4(1f, 0.2f, 0.2f, 1f), ImGuiIcon.CircleXmark),
        LogLevel.Fatal => (new Vector4(1f, 0.2f, 1f, 1f), ImGuiIcon.SkullCrossbones),
        _              => (Vector4.ONE, ImGuiIcon.FileLines)
    };

    private static uint LerpU32(uint a, uint b, float t)
    {
        var va = ImGuiNet.ColorConvertU32ToFloat4(a);
        var vb = ImGuiNet.ColorConvertU32ToFloat4(b);
        var v = LerpColor(va, vb, t);
        return ImGuiNet.ColorConvertFloat4ToU32(v);
    }

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = MathF.Max(0f, MathF.Min(1f, t));
        return new Vector4(
            a.x + (b.x - a.x) * t,
            a.y + (b.y - a.y) * t,
            a.z + (b.z - a.z) * t,
            a.w + (b.w - a.w) * t
        );
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 center, float size, bool open, uint col)
    {
        if (!open) size *= 0.75f;
        var h = size;
        var w = size * 0.9f;

        if (!open)
        {
            var p1 = new Vector2(center.x - w * 0.35f, center.y - h * 0.5f);
            var p2 = new Vector2(center.x - w * 0.35f, center.y + h * 0.5f);
            var p3 = new Vector2(center.x + w * 0.55f, center.y);
            dl.AddTriangleFilled(p1, p2, p3, col);
            return;
        }

        {
            var p1 = new Vector2(center.x - w * 0.5f, center.y - h * 0.25f);
            var p2 = new Vector2(center.x + w * 0.5f, center.y - h * 0.25f);
            var p3 = new Vector2(center.x, center.y + h * 0.55f);
            dl.AddTriangleFilled(p1, p2, p3, col);
        }
    }
    
    private static string GetFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n' || c == '\r')
                return text[..i];
        }

        return text;
    }

    private static void DrawSingleLineEllipsis(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 1f) { ImGuiNet.TextUnformatted(""); return; }
        if (ImGuiNet.CalcTextSize(text).X <= maxWidth) { ImGuiNet.TextUnformatted(text); return; }

        var ellW = ImGuiNet.CalcTextSize(C_ELLIPSE).X;
        if (ellW >= maxWidth) { ImGuiNet.TextUnformatted(C_ELLIPSE); return; }

        var lo = 0;
        var hi = text.Length;

        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            var w = ImGuiNet.CalcTextSize(text.AsSpan(0, mid).ToString()).X;
            if (w + ellW <= maxWidth) lo = mid;
            else hi = mid - 1;
        }

        var cut = lo;
        while (cut > 0 && char.IsWhiteSpace(text[cut - 1])) cut--;

        ImGuiNet.TextUnformatted(cut <= 0 ? C_ELLIPSE : text[..cut] + C_ELLIPSE);
    }
    
    private static void DrawSingleLineEllipsisForcedSuffix(string text, float maxWidth)
    {
        if (maxWidth <= 1f)
        {
            ImGuiNet.TextUnformatted("");
            return;
        }

        var sufW = ImGuiNet.CalcTextSize(C_ELLIPSE).X;
        if (sufW >= maxWidth)
        {
            ImGuiNet.TextUnformatted(C_ELLIPSE);
            return;
        }

        var lo = 0;
        var hi = text.Length;

        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            var w = ImGuiNet.CalcTextSize(text.AsSpan(0, mid).ToString()).X;
            if (w + sufW <= maxWidth) lo = mid;
            else hi = mid - 1;
        }

        var cut = lo;
        while (cut > 0 && char.IsWhiteSpace(text[cut - 1])) cut--;

        var body = cut <= 0 ? string.Empty : text[..cut];
        ImGuiNet.TextUnformatted(body + C_ELLIPSE);
    }

}
