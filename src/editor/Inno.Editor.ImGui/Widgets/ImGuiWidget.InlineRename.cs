using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a compact single-line rename editor inside an existing row.
    /// </summary>
    /// <param name="id">The stable control identifier.</param>
    /// <param name="text">The mutable text buffer.</param>
    /// <param name="requestFocus">Whether keyboard focus should be requested during this frame.</param>
    /// <param name="capacity">The maximum UTF-8 buffer capacity.</param>
    /// <param name="width">The control width, or a negative value to fill the available space.</param>
    /// <returns>The current rename outcome.</returns>
    public static InlineRenameResult InlineRename(
        string id,
        ref string text,
        ref bool requestFocus,
        nuint capacity = 512,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (requestFocus)
        {
            NativeImGui.SetKeyboardFocusHere();
            requestFocus = false;
        }

        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.SetCursorScreenPos(new Vector2(
            cursor.X,
            cursor.Y + style.inlineRenameVerticalInset));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inlineRenameFramePadding);
        NativeImGui.SetNextItemWidth(width);
        bool submitted;
        try
        {
            submitted = NativeImGui.InputText(
                $"##rename_{id}",
                ref text,
                capacity,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        }
        finally
        {
            NativeImGui.PopStyleVar();
        }
        if (NativeImGui.IsKeyPressed(ImGuiKey.Escape))
            return InlineRenameResult.Cancel;
        return submitted || NativeImGui.IsItemDeactivated()
            ? InlineRenameResult.Commit
            : InlineRenameResult.None;
    }
}
