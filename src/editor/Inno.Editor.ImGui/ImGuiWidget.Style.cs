using System.Numerics;

using Inno.Native.ImGui;

namespace Inno.Editor.ImGui;

using NativeImGui = Inno.Native.ImGui.ImGui;

/// <summary>Defines named editor layout metrics used by widgets and feature panels.</summary>
public sealed class EditorStyleMetrics
{
    /// <summary>Gets global content scale.</summary>
    public float fontScale { get; } = 1.25f;

    /// <summary>Gets disabled content opacity.</summary>
    public float disabledAlpha { get; } = 0.1f;

    /// <summary>Gets standard window padding.</summary>
    public Vector2 windowPadding { get; } = new(6f, 6f);

    /// <summary>Gets standard window rounding.</summary>
    public float windowRounding { get; } = 2f;

    /// <summary>Gets standard border thickness.</summary>
    public float borderSize { get; } = 1f;

    /// <summary>Gets minimum window size.</summary>
    public Vector2 windowMinimumSize { get; } = new(30f, 30f);

    /// <summary>Gets standard frame padding.</summary>
    public Vector2 framePadding { get; } = new(6f, 2f);

    /// <summary>Gets compact frame padding.</summary>
    public Vector2 compactFramePadding { get; } = new(4f, 1f);

    /// <summary>Gets toolbar frame padding.</summary>
    public Vector2 toolbarFramePadding { get; } = new(5f, 1f);

    /// <summary>Gets breadcrumb frame padding.</summary>
    public Vector2 breadcrumbFramePadding { get; } = new(6f, 1f);

    /// <summary>Gets standard frame rounding.</summary>
    public float frameRounding { get; } = 2f;

    /// <summary>Gets standard item spacing.</summary>
    public Vector2 itemSpacing { get; } = new(4f, 3f);

    /// <summary>Gets compact vertical item spacing.</summary>
    public Vector2 compactItemSpacing { get; } = new(4f, 2f);

    /// <summary>Gets hierarchy item spacing.</summary>
    public Vector2 hierarchyItemSpacing { get; } = new(3f, 2f);

    /// <summary>Gets standard inner item spacing.</summary>
    public Vector2 itemInnerSpacing { get; } = new(4f, 4f);

    /// <summary>Gets standard table cell padding.</summary>
    public Vector2 cellPadding { get; } = new(3f, 2f);

    /// <summary>Gets asset browser window padding.</summary>
    public Vector2 assetWindowPadding { get; } = new(2f, 1f);

    /// <summary>Gets asset browser cell padding.</summary>
    public Vector2 assetCellPadding { get; } = new(5f, 2f);

    /// <summary>Gets asset browser item spacing.</summary>
    public Vector2 assetItemSpacing { get; } = new(2f, 2f);

    /// <summary>Gets asset browser frame rounding.</summary>
    public float assetFrameRounding { get; } = 1f;

    /// <summary>Gets tree indentation.</summary>
    public float indentSpacing { get; } = 20f;

    /// <summary>Gets minimum column spacing.</summary>
    public float columnMinimumSpacing { get; } = 4f;

    /// <summary>Gets scrollbar width.</summary>
    public float scrollbarSize { get; } = 12f;

    /// <summary>Gets minimum grab size.</summary>
    public float grabMinimumSize { get; } = 12f;

    /// <summary>Gets default asset tree width.</summary>
    public float assetTreeWidth { get; } = 263f;

    /// <summary>Gets minimum asset tree width.</summary>
    public float assetTreeMinimumWidth { get; } = 140f;

    /// <summary>Gets maximum asset tree width.</summary>
    public float assetTreeMaximumWidth { get; } = 520f;

    /// <summary>Gets the minimum draggable asset browser splitter width.</summary>
    public float assetSplitterMinimumWidth { get; } = 2f;

    /// <summary>Gets asset breadcrumb bar height.</summary>
    public float assetBreadcrumbHeight { get; } = 25f;

    /// <summary>Gets minimum asset grid cell size.</summary>
    public float assetGridMinimumCellSize { get; } = 32f;

    /// <summary>Gets asset grid cell padding.</summary>
    public float assetGridCellPadding { get; } = 2f;

    /// <summary>Gets default asset grid scale.</summary>
    public float assetGridDefaultScale { get; } = 3f;

    /// <summary>Gets minimum asset grid scale.</summary>
    public float assetGridMinimumScale { get; } = 1f;

    /// <summary>Gets maximum asset grid scale.</summary>
    public float assetGridMaximumScale { get; } = 10f;

    /// <summary>Gets spacing between asset list rows.</summary>
    public float assetListRowSpacing { get; } = 2f;

    /// <summary>Gets the asset rename input width.</summary>
    public float assetRenameWidth { get; } = 320f;

    /// <summary>Gets spacing between inspector cards.</summary>
    public float inspectorCardSpacing { get; } = 3f;

    /// <summary>Gets inspector card body padding.</summary>
    public Vector2 inspectorCardBodyPadding { get; } = new(7f, 5f);

    /// <summary>Gets inspector card header padding.</summary>
    public Vector2 inspectorCardHeaderPadding { get; } = new(4f, 1f);

    /// <summary>Gets inspector disclosure inset.</summary>
    public float disclosureButtonInset { get; } = 2f;

    /// <summary>Gets minimum property label width.</summary>
    public float propertyLabelMinimumWidth { get; } = 96f;

    /// <summary>Gets maximum property label width.</summary>
    public float propertyLabelMaximumWidth { get; } = 180f;

    /// <summary>Gets property label width ratio.</summary>
    public float propertyLabelRatio { get; } = 0.40f;

    /// <summary>Gets collapsed log card spacing.</summary>
    public float logCollapsedSpacing { get; } = 1f;

    /// <summary>Gets expanded log card spacing.</summary>
    public float logExpandedSpacing { get; } = 2f;

    /// <summary>Gets the distance from the bottom treated as auto-scroll.</summary>
    public float logAutoScrollTolerance { get; } = 1f;

    /// <summary>Gets log disclosure padding.</summary>
    public Vector2 logDisclosurePadding { get; } = new(6f, 1f);

    /// <summary>Gets the default width of searchable editor popups.</summary>
    public float searchPopupWidth { get; } = 280f;

    /// <summary>Gets spacing between a leading icon and its text.</summary>
    public float iconLabelSpacing { get; } = 6f;

    /// <summary>Gets the compact checkbox size used by card headers.</summary>
    public float compactCheckboxSize { get; } = 13f;

    /// <summary>Gets spacing between leading controls inside card headers.</summary>
    public float inspectorHeaderControlSpacing { get; } = 4f;

    /// <summary>Gets top padding above inspector add buttons.</summary>
    public float inspectorAddButtonTopPadding { get; } = 7f;

    /// <summary>Gets the minimum inline hierarchy rename width.</summary>
    public float hierarchyRenameMinimumWidth { get; } = 48f;

    /// <summary>Gets the hierarchy rename trailing-control gap.</summary>
    public float hierarchyRenameTrailingGap { get; } = 8f;

    /// <summary>Gets the minimum height of the hierarchy blank drop area.</summary>
    public float hierarchyBlankMinimumHeight { get; } = 24f;

    /// <summary>Gets the tight asset browser toolbar spacing.</summary>
    public float assetToolbarTightSpacing { get; } = 2f;

    /// <summary>Gets the regular asset browser toolbar spacing.</summary>
    public float assetToolbarSpacing { get; } = 4f;

    /// <summary>Gets spacing between asset browser toolbar sections.</summary>
    public float assetToolbarSectionSpacing { get; } = 5f;

    /// <summary>Gets the asset list name-column width.</summary>
    public float assetListNameColumnWidth { get; } = 244f;

    /// <summary>Gets the asset list type-column width.</summary>
    public float assetListTypeColumnWidth { get; } = 164f;

    /// <summary>Gets horizontal padding for asset grid labels.</summary>
    public float assetGridLabelHorizontalPadding { get; } = 10f;

    /// <summary>Gets bottom padding for asset grid labels.</summary>
    public float assetGridLabelBottomPadding { get; } = 4f;

    /// <summary>Gets fixed padding added to calculated asset grid cells.</summary>
    public float assetGridFixedCellPadding { get; } = 8f;

    /// <summary>Gets the scale bias added to asset grid icons.</summary>
    public float assetGridScaleBias { get; } = 2f;

    /// <summary>Gets spacing around breadcrumb separators.</summary>
    public float assetBreadcrumbSpacing { get; } = 4f;

    /// <summary>Gets the minimum width of one axis value field.</summary>
    public float axisValueMinimumWidth { get; } = 24f;

    /// <summary>Gets the minimum width of an axis prefix.</summary>
    public float axisPrefixMinimumWidth { get; } = 18f;

    /// <summary>Gets the axis prefix share of the complete control.</summary>
    public float axisPrefixWidthRatio { get; } = 0.36f;

    /// <summary>Gets the minimum width of vector property fields.</summary>
    public float vectorFieldMinimumWidth { get; } = 42f;

    /// <summary>Gets the standard drag marker thickness.</summary>
    public float dragMarkerThickness { get; } = 2f;

    /// <summary>Gets script compilation modal width.</summary>
    public float scriptCompilationWidth { get; } = 460f;

    /// <summary>Gets the standard centered modal width.</summary>
    public float modalWidth => scriptCompilationWidth;

    /// <summary>Gets the modal fade-in duration in seconds.</summary>
    public double modalFadeInSeconds { get; } = 0.12;

    /// <summary>Gets the minimum modal visibility duration in seconds.</summary>
    public double modalMinimumVisibleSeconds { get; } = 0.35;

    /// <summary>Gets the modal fade-out duration in seconds.</summary>
    public double modalFadeOutSeconds { get; } = 0.14;

    /// <summary>Gets tree guide left offset.</summary>
    public float treeGuideLeftOffset { get; } = 1f;

    /// <summary>Gets the overlap used to join adjacent tree guide segments.</summary>
    public float treeGuideLineOverlap { get; } = 1f;

    /// <summary>Gets the minimum space between a tree disclosure and connector.</summary>
    public float treeDisclosureMinimumGap { get; } = 3f;

    /// <summary>Gets additional connector padding for expandable tree nodes.</summary>
    public float treeFolderConnectorPadding { get; } = 2f;

    /// <summary>Gets the vertical offset of text decorations from the baseline.</summary>
    public float textDecorationOffset { get; } = 2f;
}

public static partial class ImGuiWidget
{
    /// <summary>Gets centralized editor layout metrics.</summary>
    public static EditorStyleMetrics style { get; } = new();

    /// <summary>Applies the centralized editor layout and palette to ImGui.</summary>
    public static void SetupStyle()
    {
        ImGuiStylePtr nativeStyle = NativeImGui.GetStyle();
        ApplyLayoutStyle(nativeStyle);
        EditorPalette.Apply(nativeStyle);
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
        nativeStyle.WindowMenuButtonPosition = ImGuiDir.Right;
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
