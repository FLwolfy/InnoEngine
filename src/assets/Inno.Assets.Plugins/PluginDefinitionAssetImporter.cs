using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;

namespace Inno.Assets.Plugins;

[AssetImporterExtension]
internal sealed class PluginDefinitionAssetImporter : AssetImporter<PluginDefinitionAsset>
{
    public override string importerId => "inno.plugin-definition";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".iplugin"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<PluginDefinitionAsset> output,
        CancellationToken cancellationToken)
    {
        PluginDefinitionAsset asset = NativeAssetSourceSerialization.Import<PluginDefinitionAsset>(
            context.sourceBytes.Span,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        PluginExportService.ValidateDefinition(asset);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        PluginDefinitionAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PluginExportService.ValidateDefinition(asset);
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(asset));
    }
}
