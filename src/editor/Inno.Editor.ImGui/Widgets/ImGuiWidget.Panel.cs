using System;
using System.Numerics;

using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Reusable editor widgets built on top of <see cref="ImGui"/>.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Opens a standard panel window and executes panel body.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="isOpen">Visible state.</param>
    /// <param name="drawBody">Panel body callback.</param>
    /// <param name="flags">Window flags.</param>
    public static void PanelWindow(
        string title,
        ref bool isOpen,
        Action drawBody,
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse)
    {
        if (!isOpen)
            return;

        if (NativeImGui.Begin(title, flags))
        {
            if (DrawPanelCloseButton(title))
                isOpen = false;

            if (isOpen)
                drawBody();
        }

        NativeImGui.End();
    }

    private static bool DrawPanelCloseButton(string title)
    {
        if (!NativeImGui.IsWindowDocked())
            return false;

        uint dockId = NativeImGui.GetWindowDockID();
        ImGuiDockNodePtr dockNode = ImGuiP.DockBuilderGetNode(dockId);
        if (dockNode == ImGuiDockNodePtr.Null || !ImGuiP.DockNodeBeginAmendTabBar(dockNode))
            return false;

        try
        {
            ImGuiStylePtr nativeStyle = NativeImGui.GetStyle();
            float iconSlotSize = NativeImGui.GetFontSize();
            Vector2 iconSize = NativeImGui.CalcTextSize(ImGuiIcon.Xmark);
            float iconCenteringInset = MathF.Max(0f, (iconSlotSize - iconSize.X) * 0.5f);
            Vector2 itemMaximum = new(
                dockNode.Pos.X + dockNode.Size.X - nativeStyle.WindowBorderSize - nativeStyle.FramePadding.X + iconCenteringInset,
                dockNode.Pos.Y + nativeStyle.FramePadding.Y + iconSlotSize);
            Vector2 itemMinimum = itemMaximum - new Vector2(iconSlotSize);
            ImRect itemBounds = new()
            {
                Min = itemMinimum,
                Max = itemMaximum
            };
            uint itemId = NativeImGui.GetID($"##panel_close_{dockId}");
            bool hovered = false;
            bool held = false;
            ImGuiButtonFlags buttonFlags = (ImGuiButtonFlags)(
                (int)ImGuiButtonFlagsPrivate.AllowOverlap |
                (int)ImGuiButtonFlagsPrivate.NoNavFocus |
                (int)ImGuiButtonFlagsPrivate.PressedOnClickRelease);
            bool mouseHovered = NativeImGui.IsMouseHoveringRect(itemMinimum, itemMaximum);
            bool pressed = ImGuiP.ItemAdd(itemBounds, itemId) &&
                           ImGuiP.ButtonBehavior(itemBounds, itemId, ref hovered, ref held, buttonFlags);
            hovered |= mouseHovered;
            pressed |= mouseHovered && NativeImGui.IsMouseClicked(ImGuiMouseButton.Left);

            DrawIconButtonPresentation(
                NativeImGui.GetWindowDrawList(),
                itemMinimum,
                itemMaximum - itemMinimum,
                ImGuiIcon.Xmark,
                iconSize,
                hovered,
                held);

            if (hovered && NativeImGui.BeginTooltip())
            {
                NativeImGui.TextUnformatted($"Close {title}");
                NativeImGui.EndTooltip();
            }

            return pressed;
        }
        finally
        {
            ImGuiP.DockNodeEndAmendTabBar();
        }
    }

    /// <summary>
    /// Draws a disabled hint text line.
    /// </summary>
    /// <param name="text">Hint text.</param>
    public static void Hint(string text)
    {
        NativeImGui.BeginDisabled(true);
        NativeImGui.TextUnformatted(text);
        NativeImGui.EndDisabled();
    }
}
