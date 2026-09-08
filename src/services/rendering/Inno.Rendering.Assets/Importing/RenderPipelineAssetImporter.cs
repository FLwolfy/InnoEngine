using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Rendering;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class RenderPipelineAssetImporter : AssetImporter<RenderPipelineAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.pipeline";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".irenderpipeline"];

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
        AssetImportWriter<RenderPipelineAsset> output,
        CancellationToken cancellationToken)
    {
        RenderPipelineAsset asset = NativeAssetSourceSerialization.Import<RenderPipelineAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        Validate(asset);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
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
        RenderPipelineAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(asset);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }

    private static void Validate(RenderPipelineAsset asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.pipelineTypeId);
        string? duplicate = asset.features
            .GroupBy(static feature => feature.featureTypeId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Pipeline feature '{duplicate}' is configured more than once.");
        if (asset.features.Any(static feature => feature.enabled && string.IsNullOrWhiteSpace(feature.featureTypeId)))
            throw new InvalidOperationException("Every enabled pipeline feature requires a stable extension ID.");
    }
}
