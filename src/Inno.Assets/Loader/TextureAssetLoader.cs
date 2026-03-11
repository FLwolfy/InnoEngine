using System;
using System.IO;
using Inno.Assets.AssetType;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Inno.Assets.Loader;

internal sealed class TextureAssetLoader : InnoAssetLoader<TextureAsset>
{
    public override string[] validExtensions => [".png"];

    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out TextureAsset asset)
    {
        using var img = Image.Load<Rgba32>(rawBytes);

        byte[] pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);

        asset = new TextureAsset(img.Width, img.Height);

        return pixels;
    }

    protected override byte[] OnSaveSource(string assetName, in TextureAsset asset)
    {
        // You MUST have the current RGBA pixels somewhere.
        // In your pipeline, asset.assetBinaries is the decoded RGBA payload, so we use it.
        byte[]? pixels = asset.assetBinaries;
        if (pixels == null || pixels.Length == 0)
            throw new InvalidOperationException("TextureAsset has no pixel payload to write back.");

        int width = asset.width;   // adjust if your TextureAsset property names differ
        int height = asset.height;

        int expectedBytes = checked(width * height * 4);
        if (pixels.Length != expectedBytes)
        {
            throw new InvalidOperationException(
                $"TextureAsset pixel payload size mismatch. Expected {expectedBytes} bytes, got {pixels.Length}.");
        }

        // Rebuild Image<Rgba32> from RGBA bytes
        using Image<Rgba32> img = Image.LoadPixelData<Rgba32>(pixels, width, height);

        // Encode as PNG back to raw bytes
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder
        {
            // Reasonable defaults; tweak if you need smaller files vs speed.
            // CompressionLevel is optional; you can remove it if you prefer defaults.
            CompressionLevel = PngCompressionLevel.DefaultCompression
        });

        return ms.ToArray();
    }
}