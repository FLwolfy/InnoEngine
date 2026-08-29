using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Loader;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed partial class TextureAssetImporter : AssetImporter<TextureAsset>
{
    public override string importerId => "inno.rendering.texture";

    public override IReadOnlyList<string> supportedExtensions { get; } =
        [".png", ".jpg", ".jpeg", ".tga", ".hdr"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TextureAsset> output,
        CancellationToken cancellationToken)
    {
        (int width, int height) = context.extension switch
        {
            ".png" => ReadPngSize(context.sourceBytes.Span),
            ".jpg" or ".jpeg" => ReadJpegSize(context.sourceBytes.Span),
            ".tga" => ReadTgaSize(context.sourceBytes.Span),
            ".hdr" => ReadHdrSize(context.ReadUtf8Text()),
            _ => throw new RenderingAssetFormatException(context.assetPath.ToString(), "Unsupported texture container.")
        };
        var asset = new TextureAsset
        {
            width = width,
            height = height,
            colorSpace = context.extension == ".hdr" ? TextureColorSpace.Linear : TextureColorSpace.Srgb,
            sourceFormat = context.extension.TrimStart('.').ToLowerInvariant()
        };
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("source", context.sourceBytes, cancellationToken).ConfigureAwait(false);
    }

    internal static (int width, int height) ReadPngSize(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature))
        {
            throw new RenderingAssetFormatException("texture.png", "Invalid PNG signature or IHDR chunk.");
        }

        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4)));
        return RequirePositiveSize(width, height, "texture.png");
    }

    internal static (int width, int height) ReadJpegSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
        {
            throw new RenderingAssetFormatException("texture.jpg", "Invalid JPEG start marker.");
        }

        int offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                break;
            }

            byte marker = bytes[offset++];
            if (marker is 0xd8 or 0xd9)
            {
                continue;
            }

            if (offset + 2 > bytes.Length)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length)
            {
                break;
            }

            if (IsStartOfFrame(marker) && length >= 7)
            {
                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return RequirePositiveSize(width, height, "texture.jpg");
            }

            offset += length;
        }

        throw new RenderingAssetFormatException("texture.jpg", "JPEG dimensions could not be located.");
    }

    internal static (int width, int height) ReadTgaSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 18)
        {
            throw new RenderingAssetFormatException("texture.tga", "TGA header is incomplete.");
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(12, 2));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(14, 2));
        return RequirePositiveSize(width, height, "texture.tga");
    }

    internal static (int width, int height) ReadHdrSize(string text)
    {
        Match match = HdrResolutionPattern().Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out int height)
            || !int.TryParse(match.Groups[2].Value, out int width))
        {
            throw new RenderingAssetFormatException("texture.hdr", "Radiance HDR resolution line is missing.");
        }

        return RequirePositiveSize(width, height, "texture.hdr");
    }

    private static bool IsStartOfFrame(byte marker)
        => marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc);

    private static (int width, int height) RequirePositiveSize(int width, int height, string path)
        => width > 0 && height > 0
            ? (width, height)
            : throw new RenderingAssetFormatException(path, "Texture dimensions must be positive.");

    [GeneratedRegex(@"(?m)^[+-]Y\s+(\d+)\s+[+-]X\s+(\d+)\s*$")]
    private static partial Regex HdrResolutionPattern();
}
