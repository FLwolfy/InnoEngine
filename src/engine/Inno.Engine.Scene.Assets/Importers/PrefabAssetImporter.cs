using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Engine.Scene.Assets;

namespace Inno.Engine.Scene.Assets.Importers;

/// <summary>
/// Imports and exports <c>.innoprefab</c> source state.
/// </summary>
[AssetImporterExtension]
internal sealed class PrefabAssetImporter : AssetImporter<PrefabAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".innoprefab" };

    /// <inheritdoc />
    public override string importerId => "inno.engine.prefab";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <inheritdoc />
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<PrefabAsset> output,
        CancellationToken cancellationToken)
    {
        PrefabAsset asset = PrefabAsset.Import(
            context.sourceBytes.ToArray(),
            out byte[] artifact,
            out AssetDependency[] dependencies);
        for (int i = 0; i < dependencies.Length; i++)
            output.DependsOnAsset(dependencies[i]);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        PrefabAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(asset.ExportSource());
}
