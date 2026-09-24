using System;
using System.Collections.Generic;

namespace Inno.Text;

/// <summary>
/// Selects the logical direction used by the shaping engine.
/// </summary>
public enum TextDirection
{
    /// <summary>Lets the shaping engine infer direction from the text.</summary>
    Automatic,
    /// <summary>Shapes text from left to right.</summary>
    LeftToRight,
    /// <summary>Shapes text from right to left.</summary>
    RightToLeft,
    /// <summary>Shapes text from top to bottom.</summary>
    TopToBottom,
    /// <summary>Shapes text from bottom to top.</summary>
    BottomToTop
}

/// <summary>
/// Selects the typographic slant requested from a font face.
/// </summary>
public enum TextFontStyle
{
    /// <summary>Uses an upright face.</summary>
    Normal,
    /// <summary>Uses an italic face.</summary>
    Italic,
    /// <summary>Uses an oblique face.</summary>
    Oblique
}

/// <summary>
/// Identifies one font face loaded into a text backend generation.
/// </summary>
public readonly record struct TextFontHandle(ulong value)
{
    /// <summary>Gets whether this handle identifies a loaded face.</summary>
    public bool isValid => value != 0;
}

/// <summary>
/// Defines immutable font selection and sizing for one shaping operation.
/// </summary>
public readonly record struct TextStyle
{
    /// <summary>
    /// Creates validated text styling.
    /// </summary>
    /// <param name="fontSize">The positive font size in logical pixels.</param>
    /// <param name="faceIndex">The zero-based face index within the font collection.</param>
    /// <param name="weight">The requested CSS-compatible weight from 1 through 1000.</param>
    /// <param name="style">The requested face slant.</param>
    /// <param name="letterSpacing">Additional logical pixels inserted after each glyph.</param>
    public TextStyle(
        float fontSize,
        int faceIndex = 0,
        int weight = 400,
        TextFontStyle style = TextFontStyle.Normal,
        float letterSpacing = 0f)
    {
        if (!float.IsFinite(fontSize) || fontSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        if (weight is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(weight));
        if (!Enum.IsDefined(style))
            throw new ArgumentOutOfRangeException(nameof(style));
        if (!float.IsFinite(letterSpacing))
            throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        this.fontSize = fontSize;
        this.faceIndex = faceIndex;
        this.weight = weight;
        this.style = style;
        this.letterSpacing = letterSpacing;
    }

    /// <summary>Gets a 16-pixel regular default style.</summary>
    public static TextStyle defaultValue { get; } = new(16f);
    /// <summary>Gets the logical font size.</summary>
    public float fontSize { get; }
    /// <summary>Gets the collection face index.</summary>
    public int faceIndex { get; }
    /// <summary>Gets the CSS-compatible font weight.</summary>
    public int weight { get; }
    /// <summary>Gets the requested face slant.</summary>
    public TextFontStyle style { get; }
    /// <summary>Gets additional spacing appended to each glyph advance.</summary>
    public float letterSpacing { get; }
}

/// <summary>
/// Defines language and script hints for one shaping operation.
/// </summary>
public readonly record struct TextShapingOptions
{
    /// <summary>
    /// Creates shaping hints.
    /// </summary>
    /// <param name="direction">The logical text direction.</param>
    /// <param name="language">An optional BCP 47 language code.</param>
    /// <param name="script">An optional ISO 15924 script code.</param>
    public TextShapingOptions(
        TextDirection direction = TextDirection.Automatic,
        string? language = null,
        string? script = null)
    {
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        this.direction = direction;
        this.language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        this.script = string.IsNullOrWhiteSpace(script) ? null : script.Trim();
    }

    /// <summary>Gets automatic shaping hints.</summary>
    public static TextShapingOptions automatic { get; } = new();
    /// <summary>Gets the requested logical direction.</summary>
    public TextDirection direction { get; }
    /// <summary>Gets the optional BCP 47 language code.</summary>
    public string? language { get; }
    /// <summary>Gets the optional ISO 15924 script code.</summary>
    public string? script { get; }
}

/// <summary>
/// Describes one positioned glyph emitted by Unicode shaping.
/// </summary>
public readonly record struct TextGlyph(
    uint glyphId,
    uint cluster,
    float advanceX,
    float advanceY,
    float offsetX,
    float offsetY);

/// <summary>
/// Describes scalable metrics for one font face and logical size.
/// </summary>
public readonly record struct TextMetrics(
    float ascender,
    float descender,
    float lineHeight,
    float underlinePosition,
    float underlineThickness);

/// <summary>
/// Contains immutable positioned glyphs and aggregate bounds for one shaped string.
/// </summary>
public sealed class TextLayout
{
    private readonly TextGlyph[] m_glyphs;

    /// <summary>
    /// Creates an immutable text layout.
    /// </summary>
    /// <param name="glyphs">The positioned glyph sequence.</param>
    /// <param name="metrics">The face metrics used by the layout.</param>
    /// <param name="width">The logical horizontal advance.</param>
    /// <param name="height">The logical line height.</param>
    public TextLayout(IEnumerable<TextGlyph> glyphs, TextMetrics metrics, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (!float.IsFinite(width) || width < 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height < 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        m_glyphs = [..glyphs];
        this.metrics = metrics;
        this.width = width;
        this.height = height;
    }

    /// <summary>Gets the immutable positioned glyph sequence.</summary>
    public IReadOnlyList<TextGlyph> glyphs => m_glyphs;
    /// <summary>Gets the font metrics used to build this layout.</summary>
    public TextMetrics metrics { get; }
    /// <summary>Gets the aggregate horizontal advance.</summary>
    public float width { get; }
    /// <summary>Gets the aggregate line height.</summary>
    public float height { get; }
}

/// <summary>
/// Contains one rasterized glyph as tightly packed 8-bit coverage.
/// </summary>
public sealed class GlyphBitmap
{
    private readonly byte[] m_pixels;

    /// <summary>
    /// Creates a validated immutable glyph bitmap.
    /// </summary>
    /// <param name="width">The bitmap width in pixels.</param>
    /// <param name="height">The bitmap height in pixels.</param>
    /// <param name="bearingX">The horizontal bearing in pixels.</param>
    /// <param name="bearingY">The vertical bearing in pixels.</param>
    /// <param name="advanceX">The horizontal glyph advance.</param>
    /// <param name="pixels">Tightly packed row-major 8-bit coverage.</param>
    public GlyphBitmap(int width, int height, int bearingX, int bearingY, float advanceX, ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (pixels.Length != checked(width * height))
            throw new ArgumentException("Glyph coverage length must match its dimensions.", nameof(pixels));
        if (!float.IsFinite(advanceX))
            throw new ArgumentOutOfRangeException(nameof(advanceX));
        this.width = width;
        this.height = height;
        this.bearingX = bearingX;
        this.bearingY = bearingY;
        this.advanceX = advanceX;
        m_pixels = pixels.ToArray();
    }

    /// <summary>Gets the bitmap width in pixels.</summary>
    public int width { get; }
    /// <summary>Gets the bitmap height in pixels.</summary>
    public int height { get; }
    /// <summary>Gets the horizontal bearing in pixels.</summary>
    public int bearingX { get; }
    /// <summary>Gets the vertical bearing in pixels.</summary>
    public int bearingY { get; }
    /// <summary>Gets the horizontal glyph advance.</summary>
    public float advanceX { get; }
    /// <summary>Gets tightly packed row-major 8-bit coverage.</summary>
    public ReadOnlyMemory<byte> pixels => m_pixels;
}
