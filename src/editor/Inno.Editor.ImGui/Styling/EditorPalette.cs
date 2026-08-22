using System;
using System.Numerics;

using Inno.Native.ImGui;

namespace Inno.Editor.ImGui;

/// <summary>Defines the complete editor color palette in one theme surface.</summary>
public static class EditorPalette
{
    /// <summary>Gets fully transparent color.</summary>
    public static Vector4 transparent { get; } = new(0f, 0f, 0f, 0f);

    /// <summary>Gets the primary text color.</summary>
    public static Vector4 text { get; } = new(1f, 1f, 1f, 1f);

    /// <summary>Gets disabled text color.</summary>
    public static Vector4 textDisabled { get; } = new(1f, 1f, 1f, 0.360515f);

    /// <summary>Gets the standard error color.</summary>
    public static Vector4 error { get; } = new(1f, 0.35f, 0.35f, 1f);

    /// <summary>Gets the standard warning color.</summary>
    public static Vector4 warning { get; } = new(0.9f, 0.65f, 0.25f, 1f);

    /// <summary>Gets the editor window background.</summary>
    public static Vector4 windowBackground { get; } = new(0.18f, 0.18f, 0.18f, 1f);

    /// <summary>Gets the editor popup background.</summary>
    public static Vector4 popupBackground { get; } = new(0.09803922f, 0.09803922f, 0.09803922f, 1f);

    /// <summary>Gets text color used by editor context menus.</summary>
    public static Vector4 menuText => text;

    /// <summary>Gets the background used by editor context menus.</summary>
    public static Vector4 menuBackground => popupBackground;

    /// <summary>Gets the resting background of an editor context-menu item.</summary>
    public static Vector4 menuItem => transparent;

    /// <summary>Gets the hovered background of an editor context-menu item.</summary>
    public static Vector4 menuItemHovered => accentHovered;

    /// <summary>Gets the active background of an editor context-menu item.</summary>
    public static Vector4 menuItemActive => accentActive;

    /// <summary>Gets the separator color used by editor context menus.</summary>
    public static Vector4 menuSeparator => border;

    /// <summary>Gets the standard border color.</summary>
    public static Vector4 border { get; } = new(0.32f, 0.34f, 0.37f, 0.65f);

    /// <summary>Gets the standard border shadow color.</summary>
    public static Vector4 borderShadow { get; } = new(0f, 0f, 0f, 0.45f);

    /// <summary>Gets the standard frame background.</summary>
    public static Vector4 frame { get; } = new(0.15686275f, 0.15686275f, 0.15686275f, 1f);

    /// <summary>Gets the hovered frame background.</summary>
    public static Vector4 frameHovered { get; } = new(0.38039216f, 0.42352942f, 0.57254905f, 0.54901963f);

    /// <summary>Gets the active frame background.</summary>
    public static Vector4 frameActive { get; } = new(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);

    /// <summary>Gets the inactive title background.</summary>
    public static Vector4 title { get; } = new(0.14f, 0.14f, 0.14f, 1f);

    /// <summary>Gets the active title background.</summary>
    public static Vector4 titleActive { get; } = new(0.18f, 0.18f, 0.18f, 1f);

    /// <summary>Gets the collapsed title background.</summary>
    public static Vector4 titleCollapsed { get; } = new(0.12f, 0.12f, 0.12f, 1f);

    /// <summary>Gets the standard scrollbar thumb.</summary>
    public static Vector4 scrollbarGrab { get; } = new(0.15686275f, 0.15686275f, 0.15686275f, 1f);

    /// <summary>Gets the hovered scrollbar thumb.</summary>
    public static Vector4 scrollbarGrabHovered { get; } = new(0.23529412f, 0.23529412f, 0.23529412f, 1f);

    /// <summary>Gets the active scrollbar thumb.</summary>
    public static Vector4 scrollbarGrabActive { get; } = new(0.29411766f, 0.29411766f, 0.29411766f, 1f);

    /// <summary>Gets the standard accent color.</summary>
    public static Vector4 accent { get; } = new(0.61960787f, 0.5764706f, 0.76862746f, 0.54901963f);

    /// <summary>Gets the hovered accent color.</summary>
    public static Vector4 accentHovered { get; } = new(0.7372549f, 0.69411767f, 0.8862745f, 0.54901963f);

    /// <summary>Gets the active accent color.</summary>
    public static Vector4 accentActive { get; } = new(0.8156863f, 0.77254903f, 0.9647059f, 0.54901963f);

    /// <summary>Gets the standard tab color.</summary>
    public static Vector4 tab { get; } = new(0.44f, 0.38f, 0.58f, 0.65f);

    /// <summary>Gets the hovered tab color.</summary>
    public static Vector4 tabHovered { get; } = new(0.54f, 0.48f, 0.70f, 0.85f);

    /// <summary>Gets the selected tab color.</summary>
    public static Vector4 tabSelected { get; } = new(0.62f, 0.56f, 0.80f, 0.95f);

    /// <summary>Gets the dimmed tab color.</summary>
    public static Vector4 tabDimmed { get; } = new(0.32f, 0.29f, 0.42f, 0.60f);

    /// <summary>Gets the selected dimmed tab color.</summary>
    public static Vector4 tabDimmedSelected { get; } = new(0.50f, 0.45f, 0.67f, 0.80f);

    /// <summary>Gets the selected tab overline color.</summary>
    public static Vector4 tabSelectedOverline { get; } = new(0.72f, 0.66f, 0.90f, 1f);

    /// <summary>Gets the table header background.</summary>
    public static Vector4 tableHeader { get; } = new(0.1882353f, 0.1882353f, 0.2f, 1f);

    /// <summary>Gets the strong table border.</summary>
    public static Vector4 tableBorderStrong { get; } = new(0.42352942f, 0.38039216f, 0.57254905f, 0.54901963f);

    /// <summary>Gets the light table border.</summary>
    public static Vector4 tableBorderLight { get; } = new(0.42352942f, 0.38039216f, 0.57254905f, 0.2918455f);

    /// <summary>Gets the alternate table row background.</summary>
    public static Vector4 tableRowAlternate { get; } = new(1f, 1f, 1f, 0.03433478f);

    /// <summary>Gets the standard drag target color.</summary>
    public static Vector4 dragDropTarget { get; } = new(1f, 1f, 0f, 0.9f);

    /// <summary>Gets navigation highlight color.</summary>
    public static Vector4 navigationHighlight { get; } = new(1f, 1f, 1f, 0.7f);

    /// <summary>Gets navigation dim background.</summary>
    public static Vector4 navigationDim { get; } = new(0.8f, 0.8f, 0.8f, 0.2f);

    /// <summary>Gets modal dim background.</summary>
    public static Vector4 modalDim { get; } = new(0.8f, 0.8f, 0.8f, 0.35f);

    /// <summary>Gets inspector card header background.</summary>
    public static Vector4 inspectorCardHeader { get; } = new(0.42f, 0.39f, 0.51f, 1f);

    /// <summary>Gets the persistent Inspector target header background.</summary>
    public static Vector4 inspectorTargetHeader { get; } = new(0.14f, 0.14f, 0.16f, 1f);

    /// <summary>Gets the persistent Inspector target header border.</summary>
    public static Vector4 inspectorTargetHeaderBorder { get; } = new(0.30f, 0.29f, 0.34f, 1f);

    /// <summary>Gets inspector card body background.</summary>
    public static Vector4 inspectorCardBody { get; } = new(0.12f, 0.12f, 0.14f, 1f);

    /// <summary>Gets inspector card body border.</summary>
    public static Vector4 inspectorCardBodyBorder { get; } = new(0.28f, 0.27f, 0.32f, 1f);

    /// <summary>Gets disabled inspector card text.</summary>
    public static Vector4 inspectorCardDisabledText { get; } = new(0.52f, 0.52f, 0.54f, 1f);

    /// <summary>Gets inspector disclosure hover background.</summary>
    public static Vector4 inspectorCardDisclosureHovered { get; } = new(0.24f, 0.22f, 0.31f, 1f);

    /// <summary>Gets compact control hover color.</summary>
    public static Vector4 compactControlHovered { get; } = new(0.76f, 0.69f, 0.94f, 1f);

    /// <summary>Gets the deepest collection background.</summary>
    public static Vector4 collectionHeader { get; } = new(0.165f, 0.165f, 0.165f, 1f);

    /// <summary>Gets the primary collection row background.</summary>
    public static Vector4 collectionRow { get; } = new(0.185f, 0.185f, 0.185f, 1f);

    /// <summary>Gets the alternate collection row background.</summary>
    public static Vector4 collectionRowAlternate { get; } = new(0.215f, 0.215f, 0.215f, 1f);

    /// <summary>Gets the asset browser field background.</summary>
    public static Vector4 assetField { get; } = new(0.235f, 0.22f, 0.27f, 1f);

    /// <summary>Gets the asset browser border.</summary>
    public static Vector4 assetBorder { get; } = new(0.31f, 0.30f, 0.35f, 1f);

    /// <summary>Gets the soft asset browser border.</summary>
    public static Vector4 assetBorderSoft { get; } = new(0.24f, 0.24f, 0.27f, 1f);

    /// <summary>Gets the asset browser text color.</summary>
    public static Vector4 assetText { get; } = new(0.86f, 0.86f, 0.86f, 1f);

    /// <summary>Gets the muted asset browser text color.</summary>
    public static Vector4 assetTextMuted { get; } = new(0.54f, 0.54f, 0.56f, 1f);

    /// <summary>Gets the subdued text color used by asset browser breadcrumb paths.</summary>
    public static Vector4 assetBreadcrumbText { get; } = new(0.86f, 0.86f, 0.86f, 0.5f);

    /// <summary>Gets the opaque asset browser accent.</summary>
    public static Vector4 assetAccent { get; } = new(0.50f, 0.45f, 0.62f, 1f);

    /// <summary>
    /// Gets a hover treatment derived from a base theme color.
    /// </summary>
    /// <param name="color">The base theme color.</param>
    /// <returns>The base color blended toward the palette text color using the standard hover amount.</returns>
    public static Vector4 GetHovered(Vector4 color) => Lerp(color, text, 0.16f);

    /// <summary>
    /// Gets an active treatment derived from a base theme color.
    /// </summary>
    /// <param name="color">The base theme color.</param>
    /// <returns>The base color blended toward the palette text color using the standard active amount.</returns>
    public static Vector4 GetActive(Vector4 color) => Lerp(color, text, 0.24f);

    /// <summary>Gets scene row background.</summary>
    public static Vector4 hierarchySceneRow { get; } = new(28f / 255f, 26f / 255f, 25f / 255f, 1f);

    /// <summary>Gets inactive hierarchy text.</summary>
    public static Vector4 hierarchyInactiveText { get; } = new(0.52f, 0.52f, 0.54f, 1f);

    /// <summary>Gets hierarchy tree guide color.</summary>
    public static Vector4 treeGuide { get; } = new(0.62f, 0.62f, 0.66f, 1f);

    /// <summary>Gets X axis color.</summary>
    public static Vector4 axisX { get; } = new(0.76f, 0.20f, 0.22f, 1f);

    /// <summary>Gets Y axis color.</summary>
    public static Vector4 axisY { get; } = new(0.16f, 0.62f, 0.30f, 1f);

    /// <summary>Gets Z axis color.</summary>
    public static Vector4 axisZ { get; } = new(0.20f, 0.34f, 0.78f, 1f);

    /// <summary>Gets W axis color.</summary>
    public static Vector4 axisW { get; } = new(0.48f, 0.48f, 0.50f, 1f);

    /// <summary>Gets debug log color.</summary>
    public static Vector4 logDebug { get; } = new(0.80f, 0.90f, 0.85f, 1f);

    /// <summary>Gets informational log color.</summary>
    public static Vector4 logInfo { get; } = new(0.20f, 1f, 0.20f, 1f);

    /// <summary>Gets warning log color.</summary>
    public static Vector4 logWarning { get; } = new(1f, 1f, 0.20f, 1f);

    /// <summary>Gets error log color.</summary>
    public static Vector4 logError { get; } = new(1f, 0.20f, 0.20f, 1f);

    /// <summary>Gets fatal log color.</summary>
    public static Vector4 logFatal { get; } = new(1f, 0.20f, 1f, 1f);

    /// <summary>Gets collapsed log card background.</summary>
    public static Vector4 logCollapsedCard { get; } = new(0.61960787f, 0.5764706f, 0.76862746f, 0.3019608f);

    /// <summary>Gets collapsed log card border.</summary>
    public static Vector4 logCollapsedBorder { get; } = new(0.32f, 0.34f, 0.37f, 0.4225f);

    /// <summary>Gets the base expanded log card background.</summary>
    public static Vector4 logExpandedBase { get; } = new(0.10f, 0.10f, 0.10f, 1f);

    /// <summary>Gets the base expanded log card border.</summary>
    public static Vector4 logExpandedBorderBase { get; } = new(0.24f, 0.24f, 0.24f, 1f);

    /// <summary>Gets log header button background.</summary>
    public static Vector4 logToggle { get; } = new(0.61960787f, 0.5764706f, 0.76862746f, 0f);

    /// <summary>Gets hovered log header button background.</summary>
    public static Vector4 logToggleHovered { get; } = new(0.7372549f, 0.69411767f, 0.8862745f, 0.4501961f);

    /// <summary>Gets active log header button background.</summary>
    public static Vector4 logToggleActive { get; } = new(0.8156863f, 0.77254903f, 0.9647059f, 0.4501961f);

    /// <summary>
    /// Gets the expanded log card background derived from a severity color.
    /// </summary>
    /// <param name="severityColor">The palette color representing the log severity.</param>
    /// <returns>The standard expanded-card base blended with the severity color.</returns>
    public static Vector4 GetLogExpandedCard(Vector4 severityColor)
        => Lerp(logExpandedBase, severityColor, 0.12f);

    /// <summary>
    /// Gets the expanded log card border derived from a severity color.
    /// </summary>
    /// <param name="severityColor">The palette color representing the log severity.</param>
    /// <returns>The standard expanded-border base blended with the severity color.</returns>
    public static Vector4 GetLogExpandedBorder(Vector4 severityColor)
        => Lerp(logExpandedBorderBase, severityColor, 0.20f);

    /// <summary>
    /// Gets the separator color used inside an expanded log card.
    /// </summary>
    /// <param name="cardColor">The resolved background color of the expanded card.</param>
    /// <returns>The card color blended toward the palette text color.</returns>
    public static Vector4 GetLogSeparator(Vector4 cardColor)
        => Lerp(cardColor, text, 0.12f);

    /// <summary>
    /// Linearly interpolates two palette colors using a clamped amount.
    /// </summary>
    /// <param name="from">The color returned when <paramref name="amount"/> is zero.</param>
    /// <param name="to">The color returned when <paramref name="amount"/> is one.</param>
    /// <param name="amount">The interpolation amount, clamped to the inclusive zero-to-one range.</param>
    /// <returns>The component-wise interpolated color.</returns>
    public static Vector4 Lerp(Vector4 from, Vector4 to, float amount)
    {
        float value = Math.Clamp(amount, 0f, 1f);
        return new Vector4(
            from.X + (to.X - from.X) * value,
            from.Y + (to.Y - from.Y) * value,
            from.Z + (to.Z - from.Z) * value,
            from.W + (to.W - from.W) * value);
    }

    internal static void Apply(ImGuiStylePtr style)
    {
        style.Colors[(int)ImGuiCol.Text] = text;
        style.Colors[(int)ImGuiCol.TextDisabled] = textDisabled;
        style.Colors[(int)ImGuiCol.WindowBg] = windowBackground;
        style.Colors[(int)ImGuiCol.ChildBg] = transparent;
        style.Colors[(int)ImGuiCol.PopupBg] = popupBackground;
        style.Colors[(int)ImGuiCol.Border] = border;
        style.Colors[(int)ImGuiCol.BorderShadow] = borderShadow;
        style.Colors[(int)ImGuiCol.FrameBg] = frame;
        style.Colors[(int)ImGuiCol.FrameBgHovered] = frameHovered;
        style.Colors[(int)ImGuiCol.FrameBgActive] = frameActive;
        style.Colors[(int)ImGuiCol.TitleBg] = title;
        style.Colors[(int)ImGuiCol.TitleBgActive] = titleActive;
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = titleCollapsed;
        style.Colors[(int)ImGuiCol.MenuBarBg] = transparent;
        style.Colors[(int)ImGuiCol.ScrollbarBg] = WithAlpha(frame, 0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = scrollbarGrab;
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = scrollbarGrabHovered;
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = scrollbarGrabActive;
        style.Colors[(int)ImGuiCol.CheckMark] = scrollbarGrabActive;
        style.Colors[(int)ImGuiCol.SliderGrab] = accent;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accentActive;
        style.Colors[(int)ImGuiCol.Button] = accent;
        style.Colors[(int)ImGuiCol.ButtonHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.ButtonActive] = accentActive;
        style.Colors[(int)ImGuiCol.Header] = accent;
        style.Colors[(int)ImGuiCol.HeaderHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.HeaderActive] = accentActive;
        style.Colors[(int)ImGuiCol.Separator] = accent;
        style.Colors[(int)ImGuiCol.SeparatorHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.SeparatorActive] = accentActive;
        style.Colors[(int)ImGuiCol.ResizeGrip] = accent;
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.ResizeGripActive] = accentActive;
        style.Colors[(int)ImGuiCol.Tab] = tab;
        style.Colors[(int)ImGuiCol.TabHovered] = tabHovered;
        style.Colors[(int)ImGuiCol.TabSelected] = tabSelected;
        style.Colors[(int)ImGuiCol.TabDimmed] = tabDimmed;
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = tabDimmedSelected;
        style.Colors[(int)ImGuiCol.TabSelectedOverline] = tabSelectedOverline;
        style.Colors[(int)ImGuiCol.PlotLines] = scrollbarGrabActive;
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.PlotHistogram] = accent;
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = accentHovered;
        style.Colors[(int)ImGuiCol.TableHeaderBg] = tableHeader;
        style.Colors[(int)ImGuiCol.TableBorderStrong] = tableBorderStrong;
        style.Colors[(int)ImGuiCol.TableBorderLight] = tableBorderLight;
        style.Colors[(int)ImGuiCol.TableRowBg] = transparent;
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = tableRowAlternate;
        style.Colors[(int)ImGuiCol.TextSelectedBg] = accentHovered;
        style.Colors[(int)ImGuiCol.DragDropTarget] = dragDropTarget;
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = navigationHighlight;
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = navigationDim;
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = modalDim;
        style.Colors[(int)ImGuiCol.DockingPreview] = accentActive;
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
        => new(color.X, color.Y, color.Z, alpha);
}
