using System;
using System.Text;

using Inno.Native.Text;
using Inno.Text;

namespace Inno.Adapter.Text.FreeTypeHarfBuzz;

/// <summary>
/// Implements Unicode shaping and glyph rasterization through pinned FreeType and HarfBuzz sources.
/// </summary>
public sealed unsafe class FreeTypeHarfBuzzTextBackend : ITextBackend
{
    private InnoTextContext m_context;
    private bool m_disposed;

    /// <summary>
    /// Creates and validates a native text context.
    /// </summary>
    public FreeTypeHarfBuzzTextBackend()
    {
        InnoTextContext context = InnoTextContext.Null;
        ThrowIfFailed(TextNative.Create(&context), "create text context");
        if (context.IsNull)
            throw new InvalidOperationException("The native text backend returned a null context.");
        m_context = context;
    }

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
    public TextFontHandle LoadFont(ReadOnlySpan<byte> data, int faceIndex)
    {
        EnsureActive();
        if (data.IsEmpty)
            throw new ArgumentException("Font data cannot be empty.", nameof(data));
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        ulong handle = 0;
        ThrowIfFailed(TextNative.LoadFont(m_context, data, faceIndex, ref handle), "load font");
        return new TextFontHandle(handle);
    }

    /// <summary>
    /// Releases one loaded face.
    /// </summary>
    /// <param name="font">
    /// The face handle to release.
    /// </param>
    public void ReleaseFont(TextFontHandle font)
    {
        EnsureActive();
        if (!font.isValid)
            return;
        ThrowIfFailed(TextNative.ReleaseFont(m_context, font.value), "release font");
    }

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
    public TextLayout Shape(TextFontHandle font, string text, TextStyle style, TextShapingOptions options)
    {
        EnsureActive();
        if (!font.isValid)
            throw new ArgumentException("A valid font handle is required.", nameof(font));
        ArgumentNullException.ThrowIfNull(text);
        byte[] textBytes = Utf8(text);
        byte[] languageBytes = Utf8(options.language ?? string.Empty);
        byte[] scriptBytes = Utf8(options.script ?? string.Empty);
        nuint glyphCount = 0;
        fixed (byte* textPointer = textBytes)
        fixed (byte* languagePointer = languageBytes)
        fixed (byte* scriptPointer = scriptBytes)
        {
            ThrowIfFailed(TextNative.ShapeUtf8(
                m_context,
                font.value,
                textPointer,
                (nuint)(textBytes.Length - 1),
                style.fontSize,
                (InnoTextDirection)options.direction,
                languagePointer,
                scriptPointer,
                InnoTextGlyphPtr.Null,
                0,
                &glyphCount), "query shaped glyph count");
            InnoTextGlyph[] nativeGlyphs = new InnoTextGlyph[checked((int)glyphCount)];
            if (nativeGlyphs.Length > 0)
            {
                fixed (InnoTextGlyph* glyphPointer = nativeGlyphs)
                {
                    ThrowIfFailed(TextNative.ShapeUtf8(
                        m_context,
                        font.value,
                        textPointer,
                        (nuint)(textBytes.Length - 1),
                        style.fontSize,
                        (InnoTextDirection)options.direction,
                        languagePointer,
                        scriptPointer,
                        glyphPointer,
                        (nuint)nativeGlyphs.Length,
                        &glyphCount), "shape text");
                }
            }

            TextGlyph[] glyphs = new TextGlyph[nativeGlyphs.Length];
            float width = 0f;
            for (int index = 0; index < nativeGlyphs.Length; index++)
            {
                InnoTextGlyph glyph = nativeGlyphs[index];
                float advanceX = glyph.AdvanceX;
                float advanceY = glyph.AdvanceY;
                if (options.direction is TextDirection.TopToBottom or TextDirection.BottomToTop)
                    advanceY += MathF.CopySign(style.letterSpacing, advanceY == 0f ? 1f : advanceY);
                else
                    advanceX += MathF.CopySign(style.letterSpacing, advanceX == 0f ? 1f : advanceX);
                width += MathF.Abs(advanceX);
                glyphs[index] = new TextGlyph(
                    glyph.GlyphId,
                    glyph.Cluster,
                    advanceX,
                    advanceY,
                    glyph.OffsetX,
                    glyph.OffsetY);
            }
            InnoTextMetrics metrics = default;
            ThrowIfFailed(TextNative.GetMetrics(m_context, font.value, style.fontSize, ref metrics), "query font metrics");
            var publicMetrics = new TextMetrics(
                metrics.Ascender,
                metrics.Descender,
                metrics.LineHeight,
                metrics.UnderlinePosition,
                metrics.UnderlineThickness);
            return new TextLayout(glyphs, publicMetrics, width, MathF.Max(0f, metrics.LineHeight));
        }
    }

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
    public GlyphBitmap Rasterize(TextFontHandle font, uint glyphId, float fontSize)
    {
        EnsureActive();
        if (!font.isValid)
            throw new ArgumentException("A valid font handle is required.", nameof(font));
        if (!float.IsFinite(fontSize) || fontSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        InnoTextBitmap bitmap = default;
        ThrowIfFailed(TextNative.RasterizeGlyph(
            m_context, font.value, glyphId, fontSize, null, 0, &bitmap), "query glyph bitmap");
        byte[] pixels = new byte[checked((int)bitmap.ByteLength)];
        if (pixels.Length > 0)
        {
            fixed (byte* pixelPointer = pixels)
                ThrowIfFailed(TextNative.RasterizeGlyph(
                    m_context, font.value, glyphId, fontSize, pixelPointer, (nuint)pixels.Length, &bitmap),
                    "rasterize glyph");
        }
        return new GlyphBitmap(
            bitmap.Width,
            bitmap.Height,
            bitmap.BearingX,
            bitmap.BearingY,
            bitmap.AdvanceX,
            pixels);
    }

    /// <summary>
    /// Releases the native text context and every remaining face.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        TextNative.Destroy(m_context);
        m_context = InnoTextContext.Null;
    }

    private static byte[] Utf8(string value)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    private static void ThrowIfFailed(InnoTextResult result, string operation)
    {
        if (result != InnoTextResult.Success)
            throw new InvalidOperationException($"Failed to {operation}: {TextNative.ResultMessage(result) ?? result.ToString()}.");
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(m_disposed, this);
}
