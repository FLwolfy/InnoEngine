using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

internal static class FileBrowserUtility
{
    internal static void PushBrowserStyle()
    {
        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.assetText);
        NativeImGui.PushStyleColor(ImGuiCol.WindowBg, EditorPalette.collectionHeader);
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, EditorPalette.collectionHeader);
        NativeImGui.PushStyleColor(ImGuiCol.Border, EditorPalette.assetBorder);
        NativeImGui.PushStyleColor(ImGuiCol.TableHeaderBg, EditorPalette.collectionHeader);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderStrong, EditorPalette.assetBorder);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderLight, EditorPalette.assetBorderSoft);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBg, EditorPalette.collectionRow);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, EditorPalette.collectionRowAlternate);
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.assetAccent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.assetAccent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.assetAccent);
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ImGuiWidget.style.assetWindowPadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, ImGuiWidget.style.borderSize);
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, ImGuiWidget.style.assetCellPadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiWidget.style.assetItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ImGuiWidget.style.assetFrameRounding);
    }

    internal static void PopBrowserStyle()
    {
        NativeImGui.PopStyleVar(5);
        NativeImGui.PopStyleColor(12);
    }

    internal static void PushButtonColors(Vector4 color)
    {
        NativeImGui.PushStyleColor(ImGuiCol.Button, color);
        NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, EditorPalette.GetHovered(color));
        NativeImGui.PushStyleColor(ImGuiCol.ButtonActive, EditorPalette.GetActive(color));
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
