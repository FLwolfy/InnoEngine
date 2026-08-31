using System;
using System.Numerics;

namespace Inno.Editor.ImGui;

/// <summary>Defines named editor layout metrics used by widgets and feature panels.</summary>
public sealed class EditorStyleMetrics
{
    /// <summary>Defines the smallest supported editor UI zoom.</summary>
    public const float C_MIN_ZOOM = 0.75f;

    /// <summary>Defines the largest supported editor UI zoom.</summary>
    public const float C_MAX_ZOOM = 1.50f;

    /// <summary>Defines one keyboard or menu zoom increment.</summary>
    public const float C_ZOOM_STEP = 0.10f;

    private float m_zoom = 1f;

    /// <summary>Gets the current editor UI zoom multiplier.</summary>
    public float zoom => m_zoom;

    /// <summary>
    /// Sets the editor UI zoom after clamping it to the supported range.
    /// </summary>
    /// <param name="value">The requested UI zoom multiplier.</param>
    /// <returns><see langword="true"/> when the effective zoom changed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is not finite.
    /// </exception>
    public bool SetZoom(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "UI zoom must be finite.");
        float clamped = Math.Clamp(value, C_MIN_ZOOM, C_MAX_ZOOM);
        if (MathF.Abs(m_zoom - clamped) < 0.0001f)
            return false;
        m_zoom = clamped;
        return true;
    }

    /// <summary>Increases editor UI zoom by one bounded increment.</summary>
    /// <returns><see langword="true"/> when the zoom changed.</returns>
    public bool ZoomIn() => SetZoom(m_zoom + C_ZOOM_STEP);

    /// <summary>Decreases editor UI zoom by one bounded increment.</summary>
    /// <returns><see langword="true"/> when the zoom changed.</returns>
    public bool ZoomOut() => SetZoom(m_zoom - C_ZOOM_STEP);

    /// <summary>Restores the baseline editor UI zoom.</summary>
    /// <returns><see langword="true"/> when the zoom changed.</returns>
    public bool ResetZoom() => SetZoom(1f);

    /// <summary>Gets global content scale.</summary>
    public float fontScale => Scale(1.25f);

    /// <summary>Gets disabled content opacity.</summary>
    public float disabledAlpha => 0.1f;

    /// <summary>Gets standard window padding.</summary>
    public Vector2 windowPadding => ScaleVector(new(6f, 6f));

    /// <summary>Gets standard window rounding.</summary>
    public float windowRounding => Scale(2f);

    /// <summary>Gets standard border thickness.</summary>
    public float borderSize => Scale(1f);

    /// <summary>Gets minimum window size.</summary>
    public Vector2 windowMinimumSize => ScaleVector(new(30f, 30f));

    /// <summary>Gets standard frame padding.</summary>
    public Vector2 framePadding => ScaleVector(new(6f, 2f));

    /// <summary>Gets the uniform content padding of editor context menus.</summary>
    public Vector2 menuWindowPadding => ScaleVector(new(8f, 6f));

    /// <summary>Gets the uniform item padding of editor context menus.</summary>
    public Vector2 menuFramePadding => ScaleVector(new(8f, 3f));

    /// <summary>Gets the uniform spacing between editor context-menu items.</summary>
    public Vector2 menuItemSpacing => ScaleVector(new(4f, 2f));

    /// <summary>Gets editor context-menu corner rounding.</summary>
    public float menuRounding => Scale(3f);

    /// <summary>Gets editor context-menu border thickness.</summary>
    public float menuBorderSize => Scale(1f);

    /// <summary>Gets compact frame padding.</summary>
    public Vector2 compactFramePadding => ScaleVector(new(4f, 1f));

    /// <summary>Gets the compact inner padding of inline rename fields.</summary>
    public Vector2 inlineRenameFramePadding => ScaleVector(new(4f, 0f));

    /// <summary>Gets toolbar frame padding.</summary>
    public Vector2 toolbarFramePadding => ScaleVector(new(5f, 1f));

    /// <summary>Gets breadcrumb frame padding.</summary>
    public Vector2 breadcrumbFramePadding => ScaleVector(new(6f, 1f));

    /// <summary>Gets standard frame rounding.</summary>
    public float frameRounding => Scale(2f);

    /// <summary>Gets standard item spacing.</summary>
    public Vector2 itemSpacing => ScaleVector(new(4f, 3f));

    /// <summary>Gets compact vertical item spacing.</summary>
    public Vector2 compactItemSpacing => ScaleVector(new(4f, 2f));

    /// <summary>Gets hierarchy item spacing.</summary>
    public Vector2 hierarchyItemSpacing => ScaleVector(new(3f, 2f));

    /// <summary>Gets standard inner item spacing.</summary>
    public Vector2 itemInnerSpacing => ScaleVector(new(4f, 4f));

    /// <summary>Gets standard table cell padding.</summary>
    public Vector2 cellPadding => ScaleVector(new(3f, 2f));

    /// <summary>Gets asset browser window padding.</summary>
    public Vector2 assetWindowPadding => ScaleVector(new(2f, 1f));

    /// <summary>Gets asset browser cell padding.</summary>
    public Vector2 assetCellPadding => ScaleVector(new(5f, 2f));

    /// <summary>Gets asset browser item spacing.</summary>
    public Vector2 assetItemSpacing => ScaleVector(new(2f, 2f));

    /// <summary>Gets asset browser frame rounding.</summary>
    public float assetFrameRounding => Scale(1f);

    /// <summary>Gets tree indentation.</summary>
    public float indentSpacing => Scale(20f);

    /// <summary>Gets minimum column spacing.</summary>
    public float columnMinimumSpacing => Scale(4f);

    /// <summary>Gets scrollbar width.</summary>
    public float scrollbarSize => Scale(12f);

    /// <summary>Gets minimum grab size.</summary>
    public float grabMinimumSize => Scale(12f);

    /// <summary>
    /// Gets the minimum visible width retained for either asset browser pane while its splitter is dragged.
    /// </summary>
    public float assetPaneMinimumVisibleWidth => Scale(16f);

    /// <summary>Gets the minimum draggable asset browser splitter width.</summary>
    public float assetSplitterMinimumWidth => Scale(2f);

    /// <summary>Gets asset breadcrumb bar height.</summary>
    public float assetBreadcrumbHeight => Scale(25f);

    /// <summary>Gets minimum asset grid cell size.</summary>
    public float assetGridMinimumCellSize => Scale(32f);

    /// <summary>Gets asset grid cell padding.</summary>
    public float assetGridCellPadding => Scale(2f);

    /// <summary>Gets default asset grid scale.</summary>
    public float assetGridDefaultScale => 3f;

    /// <summary>Gets minimum asset grid scale.</summary>
    public float assetGridMinimumScale => 1f;

    /// <summary>Gets maximum asset grid scale.</summary>
    public float assetGridMaximumScale => 10f;

    /// <summary>Gets spacing between asset list rows.</summary>
    public float assetListRowSpacing => Scale(2f);

    /// <summary>Gets spacing between inspector cards.</summary>
    public float inspectorCardSpacing => Scale(3f);

    /// <summary>Gets the inner padding of the persistent Inspector target header.</summary>
    public Vector2 inspectorTargetHeaderPadding => ScaleVector(new(6f, 4f));

    /// <summary>Gets spacing between the two rows of the persistent Inspector target header.</summary>
    public float inspectorTargetHeaderRowSpacing => Scale(1f);

    /// <summary>Gets the font-size multiplier used by the large Inspector target icon.</summary>
    public float inspectorTargetIconScale => 1.65f;

    /// <summary>Gets inspector card body padding.</summary>
    public Vector2 inspectorCardBodyPadding => ScaleVector(new(7f, 5f));

    /// <summary>Gets inspector card header padding.</summary>
    public Vector2 inspectorCardHeaderPadding => ScaleVector(new(4f, 1f));

    /// <summary>Gets inspector disclosure inset.</summary>
    public float disclosureButtonInset => Scale(2f);

    /// <summary>Gets minimum property label width.</summary>
    public float propertyLabelMinimumWidth => Scale(96f);

    /// <summary>Gets maximum property label width.</summary>
    public float propertyLabelMaximumWidth => Scale(180f);

    /// <summary>Gets property label width ratio.</summary>
    public float propertyLabelRatio => 0.40f;

    /// <summary>Gets collapsed log card spacing.</summary>
    public float logCollapsedSpacing => Scale(1f);

    /// <summary>Gets expanded log card spacing.</summary>
    public float logExpandedSpacing => Scale(2f);

    /// <summary>Gets the distance from the bottom treated as auto-scroll.</summary>
    public float logAutoScrollTolerance => Scale(1f);

    /// <summary>Gets log disclosure padding.</summary>
    public Vector2 logDisclosurePadding => ScaleVector(new(6f, 1f));

    /// <summary>Gets the default width of searchable editor popups.</summary>
    public float searchPopupWidth => Scale(280f);

    /// <summary>Gets spacing between a leading icon and its text.</summary>
    public float iconLabelSpacing => Scale(6f);

    /// <summary>Gets the compact checkbox size used by card headers.</summary>
    public float compactCheckboxSize => Scale(13f);

    /// <summary>Gets spacing between leading controls inside card headers.</summary>
    public float inspectorHeaderControlSpacing => Scale(4f);

    /// <summary>Gets the inner padding of compact colored label chips.</summary>
    public Vector2 labelChipPadding => ScaleVector(new(6f, 1f));

    /// <summary>Gets the corner rounding of compact colored label chips.</summary>
    public float labelChipRounding => frameRounding;

    /// <summary>
    /// Gets spacing between distinct control groups in an Inspector target header row.
    /// </summary>
    public float inspectorHeaderSectionSpacing => Scale(10f);

    /// <summary>Gets top padding above inspector add buttons.</summary>
    public float inspectorAddButtonTopPadding => Scale(7f);

    /// <summary>Gets the minimum inline hierarchy rename width.</summary>
    public float hierarchyRenameMinimumWidth => Scale(48f);

    /// <summary>Gets the hierarchy rename trailing-control gap.</summary>
    public float hierarchyRenameTrailingGap => Scale(8f);

    /// <summary>Gets the minimum height of the hierarchy blank drop area.</summary>
    public float hierarchyBlankMinimumHeight => Scale(24f);

    /// <summary>Gets the tight asset browser toolbar spacing.</summary>
    public float assetToolbarTightSpacing => Scale(2f);

    /// <summary>Gets the regular asset browser toolbar spacing.</summary>
    public float assetToolbarSpacing => Scale(4f);

    /// <summary>Gets spacing between asset browser toolbar sections.</summary>
    public float assetToolbarSectionSpacing => Scale(5f);

    /// <summary>Gets the default normalized position of the asset list name/type separator.</summary>
    public float assetListNameSeparatorPosition => 0.4f;

    /// <summary>Gets the default normalized position of the asset list type/source separator.</summary>
    public float assetListTypeSeparatorPosition => 0.7f;

    /// <summary>Gets the minimum normalized width reserved for each asset list column.</summary>
    public float assetListMinimumColumnRatio => 0.1f;

    /// <summary>Gets the horizontal hit width of an asset list column separator.</summary>
    public float assetListSeparatorHitWidth => Scale(8f);

    /// <summary>Gets the horizontal inset between an asset list separator and column content.</summary>
    public float assetListContentHorizontalPadding => Scale(6f);

    /// <summary>Gets horizontal padding for asset grid labels.</summary>
    public float assetGridLabelHorizontalPadding => Scale(6f);

    /// <summary>Gets bottom padding for asset grid labels.</summary>
    public float assetGridLabelBottomPadding => Scale(3f);

    /// <summary>Gets the additional vertical spacing between asset grid label lines.</summary>
    public float assetGridLabelLineSpacing => Scale(-2f);

    /// <summary>Gets fixed padding added to calculated asset grid cells.</summary>
    public float assetGridFixedCellPadding => Scale(8f);

    /// <summary>Gets the scale bias added to asset grid icons.</summary>
    public float assetGridScaleBias => 2f;

    /// <summary>Gets the top inset reserved above an asset grid icon.</summary>
    public float assetGridIconTopPadding => Scale(6f);

    /// <summary>Gets the horizontal inset that constrains an asset grid icon inside its card.</summary>
    public float assetGridIconHorizontalPadding => Scale(6f);

    /// <summary>Gets the vertical spacing between an asset grid icon and its label.</summary>
    public float assetGridIconLabelSpacing => Scale(4f);

    /// <summary>Gets spacing around breadcrumb separators.</summary>
    public float assetBreadcrumbSpacing => Scale(4f);

    /// <summary>Gets the minimum width of one axis value field.</summary>
    public float axisValueMinimumWidth => Scale(24f);

    /// <summary>Gets the minimum width of an axis prefix.</summary>
    public float axisPrefixMinimumWidth => Scale(18f);

    /// <summary>Gets the axis prefix share of the complete control.</summary>
    public float axisPrefixWidthRatio => 0.36f;

    /// <summary>Gets the standard foreground interaction-overlay thickness.</summary>
    public float interactionOverlayThickness => Scale(2f);

    /// <summary>Gets script compilation modal width.</summary>
    public float scriptCompilationWidth => Scale(460f);

    /// <summary>Gets the standard centered modal width.</summary>
    public float modalWidth => scriptCompilationWidth;

    /// <summary>Gets the modal fade-in duration in seconds.</summary>
    public double modalFadeInSeconds { get; } = 0.12;

    /// <summary>Gets the minimum modal visibility duration in seconds.</summary>
    public double modalMinimumVisibleSeconds { get; } = 0.35;

    /// <summary>Gets the modal fade-out duration in seconds.</summary>
    public double modalFadeOutSeconds { get; } = 0.14;

    /// <summary>Gets tree guide left offset.</summary>
    public float treeGuideLeftOffset => Scale(1f);

    /// <summary>Gets the overlap used to join adjacent tree guide segments.</summary>
    public float treeGuideLineOverlap => Scale(1f);

    /// <summary>Gets additional connector padding for expandable tree nodes.</summary>
    public float treeFolderConnectorPadding => Scale(2f);

    /// <summary>Gets the vertical offset of text decorations from the baseline.</summary>
    public float textDecorationOffset => Scale(2f);

    private float Scale(float value) => value * m_zoom;

    private Vector2 ScaleVector(Vector2 value) => value * m_zoom;
}
