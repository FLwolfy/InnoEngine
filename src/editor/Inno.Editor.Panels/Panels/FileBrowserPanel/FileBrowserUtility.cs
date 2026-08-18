using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using static Inno.Editor.Panels.FileBrowserPanel;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

internal static class FileBrowserUtility
{
    internal static void PushBrowserStyle()
    {
        NativeImGui.PushStyleColor(ImGuiCol.Text, S_TEXT);
        NativeImGui.PushStyleColor(ImGuiCol.WindowBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.Border, S_BORDER);
        NativeImGui.PushStyleColor(ImGuiCol.TableHeaderBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderStrong, S_BORDER);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderLight, S_BORDER_SOFT);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBg, S_BG_ROW);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, S_BG_ROW_ALT);
        NativeImGui.PushStyleColor(ImGuiCol.Header, S_ACCENT);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, S_ACCENT);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, S_ACCENT);
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f, 1f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 1f);
    }

    internal static void PopBrowserStyle()
    {
        NativeImGui.PopStyleVar(5);
        NativeImGui.PopStyleColor(12);
    }

    internal static void PushButtonColors(Vector4 color)
    {
        NativeImGui.PushStyleColor(ImGuiCol.Button, color);
        NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, LerpColor(color, Vector4.One, 0.16f));
        NativeImGui.PushStyleColor(ImGuiCol.ButtonActive, LerpColor(color, Vector4.One, 0.24f));
    }

    internal static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            1f);
    }

    internal static bool IsDirectoryPath(string relativePath)
        => AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry) && entry.isDirectory;

    internal static string GetDirectoryLabel(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "Assets";
        string name = Path.GetFileName(relativePath);
        return string.IsNullOrEmpty(name) ? "Assets" : name;
    }

    internal static string GetSourceText(AssetFileEntry entry, string currentDirectory)
    {
        string? directory = Path.GetDirectoryName(entry.relativePath)?.Replace('\\', '/');
        currentDirectory = NormalizePath(currentDirectory);
        directory = string.IsNullOrEmpty(directory) ? string.Empty : NormalizePath(directory);
        if (string.Equals(directory, currentDirectory, StringComparison.Ordinal))
            return "~";
        if (string.IsNullOrEmpty(currentDirectory))
            return string.IsNullOrEmpty(directory) ? "~" : $"~/{directory}";
        string prefix = currentDirectory + "/";
        if (directory.StartsWith(prefix, StringComparison.Ordinal))
        {
            string relativeSource = directory[prefix.Length..];
            return string.IsNullOrEmpty(relativeSource) ? "~" : $"~/{relativeSource}";
        }
        return string.IsNullOrEmpty(directory) ? "~" : $"~/{directory}";
    }

    internal static string GetTypeText(AssetFileEntry entry)
    {
        if (entry.isDirectory)
            return "FOLDER";
        string extension = string.IsNullOrEmpty(entry.extension)
            ? Path.GetExtension(entry.relativePath)
            : entry.extension;
        return string.IsNullOrEmpty(extension) ? "FILE" : extension.TrimStart('.').ToUpperInvariant();
    }

    internal static string GetFileIcon(string relativePath)
        => string.Equals(Path.GetExtension(relativePath), ".png", StringComparison.OrdinalIgnoreCase)
            ? ImGuiIcon.FileImage
            : ImGuiIcon.File;

    internal static IReadOnlyList<(string Label, string Path)> BuildBreadcrumbParts(string relativePath)
    {
        List<(string Label, string Path)> parts = [("Assets", string.Empty)];
        if (string.IsNullOrEmpty(relativePath))
            return parts;
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string path = string.Empty;
        for (int i = 0; i < segments.Length; i++)
        {
            path = string.IsNullOrEmpty(path) ? segments[i] : $"{path}/{segments[i]}";
            parts.Add((segments[i], path));
        }
        return parts;
    }

    internal static string NormalizePath(string? path)
        => string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim('/');

    internal static string GetParentDirectory(string relativePath)
    {
        string? directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        return string.IsNullOrEmpty(directory) ? string.Empty : NormalizePath(directory);
    }

    internal static string[] FitTextToLines(string text, float maxWidth, int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        if (string.IsNullOrEmpty(text))
            return [string.Empty];
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());

        List<string> lines = new(maxLines);
        int offset = 0;
        for (int lineIndex = 0; lineIndex < maxLines && offset < elements.Count; lineIndex++)
        {
            string remaining = string.Concat(elements.GetRange(offset, elements.Count - offset));
            if (NativeImGui.CalcTextSize(remaining).X <= maxWidth)
            {
                lines.Add(remaining);
                break;
            }
            bool isLastLine = lineIndex == maxLines - 1;
            string suffix = isLastLine ? "..." : string.Empty;
            int count = 0;
            string candidate = string.Empty;
            while (offset + count < elements.Count)
            {
                string next = candidate + elements[offset + count];
                if (NativeImGui.CalcTextSize(next + suffix).X > maxWidth)
                    break;
                candidate = next;
                count++;
            }
            if (count == 0)
            {
                lines.Add(isLastLine && NativeImGui.CalcTextSize("...").X <= maxWidth ? "..." : elements[offset]);
                offset++;
                continue;
            }
            lines.Add(candidate + suffix);
            offset += count;
        }
        return lines.Count == 0 ? [string.Empty] : lines.ToArray();
    }

    internal static bool IsAncestorOrSelf(string candidateAncestor, string path)
    {
        if (candidateAncestor.Length == 0 || string.Equals(candidateAncestor, path, StringComparison.Ordinal))
            return true;
        return path.Length > candidateAncestor.Length &&
               path.StartsWith(candidateAncestor, StringComparison.Ordinal) &&
               path[candidateAncestor.Length] == '/';
    }
}
