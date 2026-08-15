using System.Numerics;

namespace Inno.Editor.ImGui;

/// <summary>
/// Defines shared colors used by editor collection and hierarchy surfaces.
/// </summary>
public static class EditorPalette
{
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
