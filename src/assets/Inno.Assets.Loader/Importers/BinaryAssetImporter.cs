using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Loader.Importers;

[AssetImporterExtension]
internal sealed class BinaryAssetImporter : AssetImporter<BinaryAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".bytes", ".bin", ".dat"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<BinaryAsset> output,
        CancellationToken cancellationToken)
    {
        byte[] payload = context.sourceBytes.ToArray();
        var asset = new BinaryAsset(payload.Length);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", payload, cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        BinaryAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(asset.runtimePayload);
}
