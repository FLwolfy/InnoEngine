using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws an icon + text on one line.
    /// </summary>
    /// <param name="icon">Icon text (e.g. glyph, tag).</param>
    /// <param name="text">Main text.</param>
    public static void IconText(string icon, string text)
    {
        NativeImGui.TextUnformatted(icon);
        NativeImGui.SameLine();
        NativeImGui.TextUnformatted(text);
    }

    /// <summary>
    /// Draws a selectable row with an icon prefix.
    /// </summary>
    /// <param name="id">Stable item id.</param>
    /// <param name="icon">Icon text (e.g. glyph, tag).</param>
    /// <param name="label">Displayed label.</param>
    /// <param name="selected">Selection state.</param>
    /// <returns>True when clicked.</returns>
    public static bool SelectableIconRow(string id, string icon, string label, bool selected)
    {
        return NativeImGui.Selectable($"{icon} {label}##{id}", selected);
    }

    /// <summary>
    /// Draws a selectable tree node.
    /// </summary>
    /// <param name="id">Stable item id.</param>
    /// <param name="label">Displayed label.</param>
    /// <param name="selected">Selection state.</param>
    /// <param name="isLeaf">Whether node is a leaf.</param>
    /// <param name="defaultOpen">Whether node should open by default.</param>
    /// <param name="drawLines">Whether to draw tree guide lines.</param>
    /// <returns>True when opened and requires <see cref="Inno.Native.ImGui.ImGui.TreePop"/>.</returns>
    public static bool TreeNode(
        string id,
        string label,
        bool selected,
        bool isLeaf,
        bool defaultOpen = false,
        bool drawLines = false)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (isLeaf)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        else
            flags |= ImGuiTreeNodeFlags.OpenOnArrow;
        if (defaultOpen)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (drawLines)
            flags |= ImGuiTreeNodeFlags.DrawLinesToNodes;

        return NativeImGui.TreeNodeEx($"{label}##{id}", flags);
    }

    /// <summary>
    /// Draws a tree node with an icon prefix.
    /// </summary>
    /// <param name="id">Stable item id.</param>
    /// <param name="icon">Icon text (e.g. glyph, tag).</param>
    /// <param name="label">Displayed label.</param>
    /// <param name="selected">Selection state.</param>
    /// <param name="isLeaf">Whether node is a leaf.</param>
    /// <param name="defaultOpen">Whether node should open by default.</param>
    /// <param name="drawLines">Whether to draw tree guide lines.</param>
    /// <returns>True when opened and requires <see cref="Inno.Native.ImGui.ImGui.TreePop"/>.</returns>
    public static bool TreeNodeIcon(
        string id,
        string icon,
        string label,
        bool selected,
        bool isLeaf,
        bool defaultOpen = false,
        bool drawLines = false)
    {
        return TreeNode(id, $"{icon} {label}", selected, isLeaf, defaultOpen, drawLines);
    }

}
