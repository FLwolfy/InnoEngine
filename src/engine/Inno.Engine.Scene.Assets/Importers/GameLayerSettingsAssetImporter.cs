using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Loader;

namespace Inno.Engine.Scene.Assets.Importers;

/// <summary>
/// Imports and exports project layer settings stored in <c>.ilayers</c> sources.
/// </summary>
[AssetImporterExtension]
internal sealed class GameLayerSettingsAssetImporter : AssetImporter<GameLayerSettingsAsset>
{
    private static readonly IReadOnlyList<string> S_EXTENSIONS = new[] { ".ilayers" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.scene.layers";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => S_EXTENSIONS;

    /// <inheritdoc />
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<GameLayerSettingsAsset> output,
        CancellationToken cancellationToken)
    {
        GameLayerSettingsAsset asset = GameLayerSettingsAsset.Import(context.sourceBytes.Span);
        output.SetAsset(asset);
        await output.WriteArtifactAsync(
            "runtime",
            context.sourceBytes,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        GameLayerSettingsAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(asset.ExportSource());
}
