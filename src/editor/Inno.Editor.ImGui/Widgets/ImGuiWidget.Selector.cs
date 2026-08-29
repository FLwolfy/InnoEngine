using System;
using System.Numerics;

using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a compact selector control and begins an auto-sized, non-scrollable menu popup.
    /// </summary>
    /// <param name="id">Stable selector identifier in the current ImGui scope.</param>
    /// <param name="preview">Text displayed by the closed selector.</param>
    /// <param name="width">Width reserved for the selector control.</param>
    /// <param name="minimumPopupWidth">
    /// Minimum outer width of the popup, including its window padding.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the popup is open and its contents should be submitted;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="preview"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="minimumPopupWidth"/> is not positive.
    /// </exception>
    public static bool BeginMenuSelector(
        string id,
        string preview,
        float width,
        float minimumPopupWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(preview);
        if (width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Selector width must be positive.");
        if (minimumPopupWidth <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumPopupWidth),
                minimumPopupWidth,
                "Popup width must be positive.");
        }

        string popupId = $"##menu_selector_popup_{id}";
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        float height = NativeImGui.GetFrameHeight();
        Vector2 size = new(width, height);
        bool pressed = NativeImGui.InvisibleButton($"##menu_selector_{id}", size);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        bool open = NativeImGui.IsPopupOpen(popupId);
        if (pressed)
            NativeImGui.OpenPopup(popupId);

        DrawMenuSelectorFrame(minimum, size, preview, hovered, active || open);
        NativeImGui.SetNextWindowPos(
            new Vector2(minimum.X, minimum.Y + height),
            ImGuiCond.Appearing);
        NativeImGui.SetNextWindowSizeConstraints(
            new Vector2(MathF.Max(width, minimumPopupWidth), 0f),
            new Vector2(float.MaxValue));
        return BeginMenuPopup(popupId);
    }

    /// <summary>
    /// Ends a selector popup opened by <see cref="BeginMenuSelector"/>.
    /// </summary>
    public static void EndMenuSelector()
        => EndMenuPopup();

    private static void DrawMenuSelectorFrame(
        Vector2 minimum,
        Vector2 size,
        string preview,
        bool hovered,
        bool active)
    {
        ImGuiStylePtr nativeStyle = NativeImGui.GetStyle();
        uint background = NativeImGui.GetColorU32(
            active ? ImGuiCol.FrameBgActive : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        Vector2 maximum = minimum + size;
        float arrowSlot = size.Y;
        float arrowMinimumX = MathF.Max(minimum.X, maximum.X - arrowSlot);
        ImDrawFlags previewRounding = size.X <= arrowSlot
            ? ImDrawFlags.RoundCornersAll
            : ImDrawFlags.RoundCornersLeft;
        drawList.AddRectFilled(
            minimum,
            new Vector2(arrowMinimumX, maximum.Y),
            background,
            nativeStyle.FrameRounding,
            previewRounding);
        uint arrowBackground = NativeImGui.GetColorU32(
            active || hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        drawList.AddRectFilled(
            new Vector2(arrowMinimumX, minimum.Y),
            maximum,
            arrowBackground,
            nativeStyle.FrameRounding,
            size.X <= arrowSlot ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersRight);

        Vector2 textMinimum = minimum + nativeStyle.FramePadding;
        Vector2 textMaximum = new(arrowMinimumX, maximum.Y);
        drawList.PushClipRect(textMinimum, textMaximum, true);
        drawList.AddText(textMinimum, NativeImGui.GetColorU32(ImGuiCol.Text), preview);
        drawList.PopClipRect();
        ImGuiP.RenderArrow(
            drawList,
            new Vector2(
                arrowMinimumX + nativeStyle.FramePadding.Y,
                minimum.Y + nativeStyle.FramePadding.Y),
            NativeImGui.GetColorU32(ImGuiCol.Text),
            ImGuiDir.Down);
        ImGuiP.RenderFrameBorder(minimum, maximum, nativeStyle.FrameRounding);
    }
}
