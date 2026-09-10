using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;

namespace Inno.Rendering.MaterialGraph;

[AssetImporterExtension]
internal sealed class MaterialGraphAssetImporter : AssetImporter<MaterialGraphAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
public override string importerId => "inno.rendering.material-graph";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
public override IReadOnlyList<string> supportedExtensions { get; } = [".imaterialgraph"];

    /// <summary>
    /// Imports source content into a validated runtime asset and artifact set.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="output">
    /// The import output writer that receives runtime data and dependency declarations.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MaterialGraphAsset> output,
        CancellationToken cancellationToken)
    {
        MaterialGraphAsset asset = NativeAssetSourceSerialization.Import<MaterialGraphAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        if (asset.document is null)
            throw new InvalidDataException("A Material Graph asset requires a neutral graph document.");
        _ = MaterialGraphEvaluator.Commit(asset, context.serialization);
        output.SetAsset(asset);
        byte[] runtime = NativeAssetSourceSerialization.Export(asset, context.services);
        await output.WriteArtifactAsync("runtime", runtime, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a validated asset representation to its writable source mount.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        MaterialGraphAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.document is null)
            throw new InvalidOperationException("A Material Graph asset requires a neutral graph document.");
        _ = MaterialGraphEvaluator.Commit(asset, context.serialization);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(
            NativeAssetSourceSerialization.Export(asset, context.services));
    }
}
