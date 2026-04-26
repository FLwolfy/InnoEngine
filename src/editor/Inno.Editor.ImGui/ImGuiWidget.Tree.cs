using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a selectable tree node.
    /// </summary>
    /// <param name="id">Stable item id.</param>
    /// <param name="label">Displayed label.</param>
    /// <param name="selected">Selection state.</param>
    /// <param name="isLeaf">Whether node is a leaf.</param>
    /// <param name="defaultOpen">Whether node should open by default.</param>
    /// <returns>True when opened and requires <see cref="Inno.Native.ImGui.ImGui.TreePop"/>.</returns>
    public static bool TreeNode(
        string id,
        string label,
        bool selected,
        bool isLeaf,
        bool defaultOpen = false)
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

        return NativeImGui.TreeNodeEx($"{label}##{id}", flags);
    }

    /// <summary>
    /// Draws a selectable row.
    /// </summary>
    /// <param name="id">Stable item id.</param>
    /// <param name="label">Displayed label.</param>
    /// <param name="selected">Selection state.</param>
    /// <returns>True when clicked.</returns>
    public static bool SelectableRow(string id, string label, bool selected)
    {
        return NativeImGui.Selectable($"{label}##{id}", selected);
    }
}
