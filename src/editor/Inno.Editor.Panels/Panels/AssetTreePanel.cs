using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Assets.IO;
using Inno.Editor.Core;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Directory-first tree view for the current asset root.
/// </summary>
public sealed class AssetTreePanel : EditorPanel
{
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public AssetTreePanel()
        : base("asset.tree", "Asset Tree")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        DrawDirectoryRecursive(context, string.Empty, "Assets");
    }

    private static void DrawDirectoryRecursive(EditorContext context, string relativePath, string label)
    {
        IReadOnlyList<AssetFileEntry> children = AssetManager.GetFileSystemChildren(relativePath);
        bool selected = string.Equals(context.selection.selectedPath, relativePath, System.StringComparison.Ordinal);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (relativePath.Length == 0)
            NativeImGui.SetNextItemOpen(true, ImGuiCond.Once);
        bool opened = NativeImGui.TreeNodeEx($"{label}##{(relativePath.Length == 0 ? "root" : relativePath)}", flags);

        if (NativeImGui.IsItemClicked())
        {
            context.selection.SetSelectedPath(relativePath);
            context.selection.SetCurrentDirectory(relativePath);
        }

        if (!opened)
            return;

        for (int i = 0; i < children.Count; i++)
        {
            AssetFileEntry child = children[i];
            if (!child.isDirectory)
                continue;

            string name = Path.GetFileName(child.relativePath);
            DrawDirectoryRecursive(context, child.relativePath, name);
        }

        NativeImGui.TreePop();
    }
}
