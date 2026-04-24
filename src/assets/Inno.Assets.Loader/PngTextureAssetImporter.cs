using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Inno.Assets.Core;
using Inno.Assets.Types;

namespace Inno.Assets.Loader;

public sealed class PngTextureAssetImporter : AssetImporter<TextureAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".png"];

    public override AssetImportResult<TextureAsset> ImportTyped(in AssetImportContext context)
    {
        if (!TryReadPngSize(context.sourceBytes.Span, out int width, out int height))
            throw new InvalidOperationException($"Invalid PNG file: {context.relativePath}");

        var asset = new TextureAsset(width, height, channelCount: 4, encoding: "png");
        return new AssetImportResult<TextureAsset>(asset, context.sourceBytes.ToArray());
    }

    public override bool TryExportTyped(TextureAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.runtimePayload.ToArray();
        return sourceBytes.Length > 0;
    }

    private static bool TryReadPngSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 24)
            return false;

        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!data.Slice(0, 8).SequenceEqual(signature))
            return false;

        if (!data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        return width > 0 && height > 0;
    }
}
