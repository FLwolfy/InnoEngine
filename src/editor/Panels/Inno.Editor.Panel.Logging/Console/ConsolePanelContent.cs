using System;
using System.Numerics;

using Inno.Core.Logging;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

internal sealed class ConsolePanelContent
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

    internal (Vector4 color, string icon) GetLevelVisual(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => (EditorPalette.logDebug, C_ICON_BUG),
            LogLevel.Info => (EditorPalette.logInfo, C_ICON_INFO),
            LogLevel.Warn => (EditorPalette.logWarning, C_ICON_WARN),
            LogLevel.Error => (EditorPalette.logError, C_ICON_ERROR),
            LogLevel.Fatal => (EditorPalette.logFatal, C_ICON_FATAL),
            _ => (EditorPalette.text, C_ICON_DEFAULT)
        };
    }

    internal Vector4 GetCollapsedBgColor()
        => EditorPalette.logCollapsedCard;

    internal Vector4 GetCollapsedBorderColor()
        => EditorPalette.logCollapsedBorder;

    internal Vector4 GetExpandedBgColor(Vector4 levelColor)
        => EditorPalette.GetLogExpandedCard(levelColor);

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
