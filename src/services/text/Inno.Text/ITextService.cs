namespace Inno.Text;

/// <summary>
/// Provides backend-neutral Unicode shaping and glyph rasterization for imported fonts.
/// </summary>
public interface ITextService
{
    /// <summary>Shapes one Unicode string with an imported font.</summary>
    /// <param name="font">The imported font source.</param>
    /// <param name="text">The Unicode source text.</param>
    /// <param name="style">Font selection and sizing.</param>
    /// <param name="options">Language, script, and direction hints.</param>
    /// <returns>The immutable shaped layout.</returns>
    TextLayout Shape(FontAsset font, string text, TextStyle style, TextShapingOptions options);

    /// <summary>Rasterizes one glyph from an imported font.</summary>
    /// <param name="font">The imported font source.</param>
    /// <param name="faceIndex">The zero-based collection face index.</param>
    /// <param name="glyphId">The font-specific glyph identifier.</param>
    /// <param name="fontSize">The positive logical pixel size.</param>
    /// <returns>The immutable glyph bitmap.</returns>
    GlyphBitmap Rasterize(FontAsset font, int faceIndex, uint glyphId, float fontSize);
}
