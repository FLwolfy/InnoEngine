using System.Numerics;

namespace Inno.Editor.ImGui;

/// <summary>
/// Defines shared colors used by editor collection and hierarchy surfaces.
/// </summary>
public static class EditorPalette
{
    /// <summary>
    /// Gets the background color used by inspector card headers.
    /// </summary>
    public static Vector4 inspectorCardHeader { get; } = new(0.42f, 0.39f, 0.51f, 1f);

    /// <summary>
    /// Gets the background color used by inspector card bodies.
    /// </summary>
    public static Vector4 inspectorCardBody { get; } = new(0.12f, 0.12f, 0.14f, 1f);

    /// <summary>
    /// Gets the border color used by inspector card bodies.
    /// </summary>
    public static Vector4 inspectorCardBodyBorder { get; } = new(0.28f, 0.27f, 0.32f, 1f);

    /// <summary>
    /// Gets the text color used by disabled inspector cards.
    /// </summary>
    public static Vector4 inspectorCardDisabledText { get; } = new(0.52f, 0.52f, 0.54f, 1f);

    /// <summary>
    /// Gets the hover background color used by inspector card disclosure controls.
    /// </summary>
    public static Vector4 inspectorCardDisclosureHovered { get; } = new(0.24f, 0.22f, 0.31f, 1f);

    /// <summary>
    /// Gets the emphasized foreground color used by compact editor controls.
    /// </summary>
    public static Vector4 compactControlHovered { get; } = new(0.76f, 0.69f, 0.94f, 1f);

    /// <summary>
    /// Gets the deepest collection background used by headers and scene rows.
    /// </summary>
    public static Vector4 collectionHeader { get; } = new(0.165f, 0.165f, 0.165f, 1f);

    /// <summary>
    /// Gets the primary collection row background.
    /// </summary>
    public static Vector4 collectionRow { get; } = new(0.185f, 0.185f, 0.185f, 1f);

    /// <summary>
    /// Gets the alternate collection row background.
    /// </summary>
    public static Vector4 collectionRowAlternate { get; } = new(0.215f, 0.215f, 0.215f, 1f);
}
