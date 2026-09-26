using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Text.Runtime;

/// <summary>
/// Owns imported font leases and a replaceable Unicode shaping backend for one runtime session.
/// </summary>
public sealed class TextRuntime : RuntimeSubsystem, ITextService
{
    private readonly ITextBackend m_backend;
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Dictionary<FontKey, LoadedFont> m_fonts = [];
    private bool m_disposed;

    /// <summary>
    /// Creates a text runtime and assumes ownership of its backend.
    /// </summary>
    /// <param name="backend">
    /// The native shaping backend.
    /// </param>
    /// <param name="artifacts">
    /// The immutable asset artifact lookup.
    /// </param>
    public TextRuntime(ITextBackend backend, IAssetArtifactLookup artifacts)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    /// <summary>
    /// Binds the script-facing text facade for the complete runtime frame.
    /// </summary>
    /// <param name="frame">
    /// The current runtime frame.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame)
        => OwnFrameScope(EnterExecutionScope());

    /// <summary>
    /// Binds this runtime to the current asynchronous execution context.
    /// </summary>
    /// <returns>
    /// The caller-owned binding scope.
    /// </returns>
    public IDisposable EnterExecutionScope()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return TextExecutionContext.EnterScope(this);
    }

    /// <summary>
    /// Shapes one Unicode string with an imported font.
    /// </summary>
    /// <param name="font">
    /// The imported font source.
    /// </param>
    /// <param name="text">
    /// The Unicode source text.
    /// </param>
    /// <param name="style">
    /// Font selection and sizing.
    /// </param>
    /// <param name="options">
    /// Language, script, and direction hints.
    /// </param>
    /// <returns>
    /// The immutable shaped layout.
    /// </returns>
    public TextLayout Shape(FontAsset font, string text, TextStyle style, TextShapingOptions options)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        LoadedFont loaded = GetOrLoad(font, style.faceIndex);
        return m_backend.Shape(loaded.handle, text, style, options);
    }

    /// <summary>
    /// Rasterizes one glyph from an imported font.
    /// </summary>
    /// <param name="font">
    /// The imported font source.
    /// </param>
    /// <param name="faceIndex">
    /// The zero-based collection face index.
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
    public GlyphBitmap Rasterize(FontAsset font, int faceIndex, uint glyphId, float fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!float.IsFinite(fontSize) || fontSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return m_backend.Rasterize(GetOrLoad(font, faceIndex).handle, glyphId, fontSize);
    }

    /// <summary>
    /// Releases every native face and retained immutable artifact before the backend.
    /// </summary>
    protected override void OnStop()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        List<Exception> failures = [];
        foreach (LoadedFont font in m_fonts.Values)
        {
            try { m_backend.ReleaseFont(font.handle); }
            catch (Exception exception) { failures.Add(exception); }
            try { font.artifact.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        m_fonts.Clear();
        try { m_backend.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count > 0)
            throw new AggregateException("Text runtime retirement failed.", failures);
    }

    private LoadedFont GetOrLoad(FontAsset font, int faceIndex)
    {
        FontMetadata metadata = font.metadata
            ?? throw new InvalidOperationException("The font has no imported runtime metadata.");
        if (font.isMissing)
            throw new InvalidOperationException("The font asset is missing.");
        if ((uint)faceIndex >= (uint)metadata.faceCount)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        var key = new FontKey(font.identity.persistentId, font.contentVersion, faceIndex);
        if (m_fonts.TryGetValue(key, out LoadedFont? loaded))
            return loaded;

        ArtifactLease artifact = m_artifacts.AcquireArtifact(font.identity.persistentId, "font-data");
        try
        {
            byte[] bytes = File.ReadAllBytes(artifact.info.absolutePath);
            TextFontHandle handle = m_backend.LoadFont(bytes, faceIndex);
            if (!handle.isValid)
                throw new InvalidOperationException("The text backend rejected the imported font face.");
            loaded = new LoadedFont(handle, artifact);
            m_fonts.Add(key, loaded);
            return loaded;
        }
        catch
        {
            artifact.Dispose();
            throw;
        }
    }

    private readonly record struct FontKey(Guid persistentId, long version, int faceIndex);

    private sealed record LoadedFont(TextFontHandle handle, ArtifactLease artifact);
}
