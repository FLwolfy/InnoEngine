using System;
using System.IO;
using System.Text;

namespace Inno.Assets.Loader;

/// <summary>
/// Immutable import input context passed to asset importers.
/// </summary>
public readonly struct AssetImportContext
{
    /// <summary>
    /// Source path relative to asset root.
    /// </summary>
    public string relativePath { get; }
    /// <summary>
    /// Absolute source path.
    /// </summary>
    public string absolutePath { get; }
    /// <summary>
    /// Raw source bytes.
    /// </summary>
    public ReadOnlyMemory<byte> sourceBytes { get; }
    /// <summary>
    /// SHA-256 hash of <see cref="sourceBytes"/>.
    /// </summary>
    public string sourceHash { get; }

    /// <summary>
    /// Lower-case source extension (including dot).
    /// </summary>
    public string extension => Path.GetExtension(relativePath).ToLowerInvariant();

    /// <summary>
    /// Creates an import context.
    /// </summary>
    /// <param name="relativePath">Path relative to asset root.</param>
    /// <param name="absolutePath">Absolute source path.</param>
    /// <param name="sourceBytes">Source bytes.</param>
    /// <param name="sourceHash">SHA-256 source hash.</param>
    public AssetImportContext(
        string relativePath,
        string absolutePath,
        byte[] sourceBytes,
        string sourceHash)
    {
        this.relativePath = relativePath;
        this.absolutePath = absolutePath;
        this.sourceBytes = sourceBytes ?? [];
        this.sourceHash = sourceHash ?? string.Empty;
    }

    /// <summary>
    /// Reads <see cref="sourceBytes"/> as UTF-8 text.
    /// </summary>
    public string ReadUtf8Text()
    {
        string text = Encoding.UTF8.GetString(sourceBytes.Span);
        if (text.Length > 0 && text[0] == '\uFEFF')
            return text[1..];

        return text;
    }
}
