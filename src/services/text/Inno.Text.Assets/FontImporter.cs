using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;

namespace Inno.Text.Assets;

/// <summary>
/// Imports OpenType fonts into compact metadata and immutable encoded font artifacts.
/// </summary>
[AssetImporter("inno.text.font")]
public sealed class FontImporter : AssetImporter<FontAsset>
{
    /// <summary>
    /// Gets the supported OpenType source extensions.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ttf", ".otf", ".ttc", ".otc"];

    /// <summary>
    /// Validates the source container and emits runtime metadata plus encoded font data.
    /// </summary>
    /// <param name="context">The source import context.</param>
    /// <param name="output">The candidate artifact writer.</param>
    /// <param name="cancellationToken">Cancellation observed by artifact writes.</param>
    /// <returns>An operation that completes after both artifacts are staged.</returns>
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<FontAsset> output,
        CancellationToken cancellationToken)
    {
        FontMetadata metadata = ReadMetadata(context.sourceBytes.Span);
        output.SetAsset(new FontAsset());
        await output.WriteArtifactAsync(
            "runtime",
            FontMetadataCodec.Encode(metadata),
            cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("font-data", context.sourceBytes, cancellationToken).ConfigureAwait(false);
    }

    private static FontMetadata ReadMetadata(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12)
            throw new InvalidDataException("The font source is truncated.");
        uint signature = BinaryPrimitives.ReadUInt32BigEndian(source);
        int faceCount;
        if (signature == 0x74746366)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(source[8..]);
            if (count is 0 or > 65535 || source.Length < checked(12 + ((int)count * 4)))
                throw new InvalidDataException("The OpenType collection header is invalid.");
            faceCount = (int)count;
        }
        else if (signature is 0x00010000 or 0x4f54544f or 0x74727565 or 0x74797031)
        {
            faceCount = 1;
        }
        else
        {
            throw new InvalidDataException("The source is not a supported sfnt or OpenType collection.");
        }
        return new FontMetadata(faceCount, source.Length);
    }
}
