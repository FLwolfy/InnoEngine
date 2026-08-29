using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class RenderPipelineAssetImporter : AssetImporter<RenderPipelineAsset>
{
    public override string importerId => "inno.rendering.pipeline";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".irenderpipeline"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<RenderPipelineAsset> output,
        CancellationToken cancellationToken)
    {
        RenderPipelineAsset asset = NativeAssetSourceSerialization.Import<RenderPipelineAsset>(
            context.sourceBytes.Span,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        Validate(asset);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        RenderPipelineAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(asset);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(asset));
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
