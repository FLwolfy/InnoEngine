using System;

namespace Inno.Text;

/// <summary>
/// Defines the replaceable native shaping and glyph-rasterization boundary.
/// </summary>
public interface ITextBackend : IDisposable
{
    /// <summary>
    /// Loads one face from encoded OpenType data.
    /// </summary>
    /// <param name="data">
    /// The complete encoded font source.
    /// </param>
    /// <param name="faceIndex">
    /// The zero-based collection face index.
    /// </param>
    /// <returns>
    /// A generation-local face handle.
    /// </returns>
    TextFontHandle LoadFont(ReadOnlySpan<byte> data, int faceIndex);

    /// <summary>
    /// Releases one loaded face.
    /// </summary>
    /// <param name="font">
    /// The face handle to release.
    /// </param>
    void ReleaseFont(TextFontHandle font);

    /// <summary>
    /// Shapes one Unicode string into positioned glyphs.
    /// </summary>
    /// <param name="font">
    /// The loaded face.
    /// </param>
    /// <param name="text">
    /// The Unicode source text.
    /// </param>
    /// <param name="style">
    /// Font size and spacing.
    /// </param>
    /// <param name="options">
    /// Language, script, and direction hints.
    /// </param>
    /// <returns>
    /// The immutable layout.
    /// </returns>
    TextLayout Shape(TextFontHandle font, string text, TextStyle style, TextShapingOptions options);

    /// <summary>
    /// Rasterizes one shaped glyph into 8-bit coverage.
    /// </summary>
    /// <param name="font">
    /// The loaded face.
    /// </param>
    /// <param name="glyphId">
    /// The font-specific glyph identifier.
    /// </param>
    /// <param name="fontSize">
    /// The positive logical pixel size.
    /// </param>
    /// <returns>
    /// The immutable glyph bitmap.
    /// </returns>
    GlyphBitmap Rasterize(TextFontHandle font, uint glyphId, float fontSize);
}
