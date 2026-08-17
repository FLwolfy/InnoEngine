using System;
using System.Collections.Generic;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Importers;

[AssetImporterExtension]
internal sealed class BinaryAssetImporter : AssetImporter<BinaryAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".bytes", ".bin", ".dat"];

    protected override AssetImportResult<BinaryAsset> Import(AssetImportContext context)
    {
        byte[] payload = context.sourceBytes.ToArray();
        var asset = new BinaryAsset(payload.Length);
        return new AssetImportResult<BinaryAsset>(asset, payload);
    }

    protected override bool TryExport(BinaryAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = asset.runtimePayload.ToArray();
        return true;
    }
}
