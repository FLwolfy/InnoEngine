using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Assets.IO;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Asset content panel for current directory.
/// </summary>
public sealed class AssetBrowserPanel : EditorPanel
{
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public AssetBrowserPanel()
        : base("asset.browser", "Assets")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        DrawToolbar(context);
        NativeImGui.Separator();
        DrawEntries(context);
    }

    private static void DrawToolbar(EditorContext context)
    {
        bool isRoot = string.IsNullOrEmpty(context.selection.currentDirectory);
        NativeImGui.BeginDisabled(isRoot);
        if (NativeImGui.Button("Up"))
        {
            string parent = Path.GetDirectoryName(context.selection.currentDirectory)?.Replace('\\', '/') ?? string.Empty;
            context.selection.SetCurrentDirectory(parent);
            context.selection.SetSelectedPath(parent);
        }

        NativeImGui.EndDisabled();
        NativeImGui.SameLine();
        NativeImGui.TextUnformatted(string.IsNullOrEmpty(context.selection.currentDirectory)
            ? "Assets/"
            : $"Assets/{context.selection.currentDirectory}");
    }

    private static void DrawEntries(EditorContext context)
    {
        IReadOnlyList<AssetFileEntry> entries = AssetManager.GetFileSystemChildren(context.selection.currentDirectory);
        if (entries.Count == 0)
        {
            ImGuiWidget.Hint("Folder is empty.");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            AssetFileEntry entry = entries[i];
            string icon = entry.isDirectory ? "[D]" : "[F]";
            string name = Path.GetFileName(entry.relativePath);
            bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, System.StringComparison.Ordinal);
            if (ImGuiWidget.SelectableIconRow(entry.relativePath, icon, name, selected))
            {
                context.selection.SetSelectedPath(entry.relativePath);
            }

            if (entry.isDirectory
                && NativeImGui.IsItemHovered()
                && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                context.selection.SetCurrentDirectory(entry.relativePath);
                context.selection.SetSelectedPath(entry.relativePath);
            }
        }
    }
}
