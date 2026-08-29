using System;
using System.Numerics;

using Inno.Core.Scripting;
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
    /// <param name="useWindowPadding">Whether the panel body should use the current standard window padding.</param>
    [ScriptingApiIgnore]
    public static void PanelWindow(
        string title,
        ref bool isOpen,
        Action drawBody,
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse,
        bool useWindowPadding = true)
    {
        if (!isOpen)
            return;

        bool pushedPadding = false;
        bool beganWindow = false;
        try
        {
            if (!useWindowPadding)
            {
                NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
                pushedPadding = true;
            }
            bool visible = NativeImGui.Begin(title, flags);
            beganWindow = true;
            if (pushedPadding)
            {
                NativeImGui.PopStyleVar();
                pushedPadding = false;
            }
            if (visible)
            {
                if (DrawPanelCloseButton(title))
                    isOpen = false;

                if (isOpen)
                    drawBody();
            }
        }
        finally
        {
            if (pushedPadding)
                NativeImGui.PopStyleVar();
            if (beganWindow)
                NativeImGui.End();
        }
    }

    /// <summary>
    /// Draws a vertically auto-sized content region that is constrained to the current available
    /// width and cannot create an independent scroll range.
    /// </summary>
    /// <param name="id">Stable identifier used by ImGui to track the content region.</param>
    /// <param name="drawContent">Callback that draws the complete region contents.</param>
    /// <param name="useWindowPadding">
    /// Whether the region should apply the centralized standard window padding exactly once.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="drawContent"/> is <see langword="null"/>.
    /// </exception>
    public static void ConstrainedContent(
        string id,
        Action drawContent,
        bool useWindowPadding = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(drawContent);

        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        Vector2 padding = useWindowPadding
            ? NativeImGui.GetStyle().WindowPadding
            : Vector2.Zero;
        float contentWidth = MathF.Max(1f, width - padding.X * 2f);
        ImGuiChildFlags childFlags = ImGuiChildFlags.AutoResizeY |
                                     ImGuiChildFlags.AlwaysAutoResize;
        if (useWindowPadding)
            childFlags |= ImGuiChildFlags.AlwaysUseWindowPadding;
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar |
                                       ImGuiWindowFlags.NoScrollWithMouse |
                                       ImGuiWindowFlags.NoSavedSettings;
        NativeImGui.SetNextWindowContentSize(new Vector2(contentWidth, 0f));
        bool visible = NativeImGui.BeginChild(id, new Vector2(width, 0f), childFlags, windowFlags);
        try
        {
            if (visible)
                drawContent();
        }
        finally
        {
            NativeImGui.EndChild();
        }
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
            float iconSlotSize = GetCompactIconSize().X;
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

            DrawClickableTextPresentation(
                NativeImGui.GetWindowDrawList(),
                itemMinimum,
                itemMaximum - itemMinimum,
                ImGuiIcon.Xmark,
                iconSize,
                hovered,
                held);

            if (hovered && BeginMenuTooltip())
            {
                NativeImGui.TextUnformatted($"Close {title}");
                EndMenuTooltip();
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
        try
        {
            NativeImGui.TextUnformatted(text);
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }
}
