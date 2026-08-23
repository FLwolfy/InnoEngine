using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    private static float s_appliedZoom = float.NaN;

    /// <summary>
    /// Gets the centralized editor layout metrics shared by every widget and feature panel.
    /// </summary>
    public static EditorStyleMetrics style { get; } = new();

    /// <summary>
    /// Applies the centralized editor layout metrics and palette to the current ImGui context.
    /// </summary>
    public static void SetupStyle()
    {
        ImGuiStylePtr nativeStyle = NativeImGui.GetStyle();
        ApplyLayoutStyle(nativeStyle);
        EditorPalette.Apply(nativeStyle);
        s_appliedZoom = style.zoom;
    }

    internal static void ApplyPendingStyle()
    {
        if (float.IsNaN(s_appliedZoom) || MathF.Abs(s_appliedZoom - style.zoom) > 0.0001f)
            SetupStyle();
    }

    private static void ApplyLayoutStyle(ImGuiStylePtr nativeStyle)
    {
        nativeStyle.Alpha = 1f;
        nativeStyle.DisabledAlpha = style.disabledAlpha;
        nativeStyle.FontScaleMain = style.fontScale;
        nativeStyle.WindowPadding = style.windowPadding;
        nativeStyle.WindowRounding = style.windowRounding;
        nativeStyle.WindowBorderSize = style.borderSize;
        nativeStyle.WindowMinSize = style.windowMinimumSize;
        nativeStyle.WindowTitleAlign = new Vector2(0.5f, 0.5f);
        nativeStyle.WindowMenuButtonPosition = ImGuiDir.None;
        nativeStyle.ChildRounding = style.windowRounding;
        nativeStyle.ChildBorderSize = style.borderSize;
        nativeStyle.PopupRounding = style.windowRounding;
        nativeStyle.PopupBorderSize = 0f;
        nativeStyle.FramePadding = style.framePadding;
        nativeStyle.FrameRounding = style.frameRounding;
        nativeStyle.FrameBorderSize = 0f;
        nativeStyle.ItemSpacing = style.itemSpacing;
        nativeStyle.ItemInnerSpacing = style.itemInnerSpacing;
        nativeStyle.CellPadding = style.cellPadding;
        nativeStyle.IndentSpacing = style.indentSpacing;
        nativeStyle.ColumnsMinSpacing = style.columnMinimumSpacing;
        nativeStyle.ScrollbarSize = style.scrollbarSize;
        nativeStyle.ScrollbarRounding = style.frameRounding;
        nativeStyle.GrabMinSize = style.grabMinimumSize;
        nativeStyle.GrabRounding = style.frameRounding;
        nativeStyle.TabRounding = style.frameRounding;
        nativeStyle.TabBorderSize = 0f;
        nativeStyle.TabBarOverlineSize = 0f;
        nativeStyle.ColorButtonPosition = ImGuiDir.Right;
        nativeStyle.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        nativeStyle.SelectableTextAlign = Vector2.Zero;
    }
}
