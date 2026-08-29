using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Serialization;
using Inno.Core.Graphs;
using Inno.Rendering.Assets;

namespace Inno.Rendering.ShaderGraph;

[AssetImporterExtension]
internal sealed class ShaderGraphAssetImporter : AssetImporter<ShaderGraphAsset>
{
    public override string importerId => "inno.rendering.shader-graph";

    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishadergraph"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ShaderGraphAsset> output,
        CancellationToken cancellationToken)
    {
        ShaderGraphAsset asset = NativeAssetSourceSerialization.Import<ShaderGraphAsset>(
            context.sourceBytes.Span,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        GraphDocument document = asset.document
            ?? throw new InvalidDataException("A Shader Graph asset requires a neutral graph document.");
        using var registry = new ShaderNodeRegistry(discoverExtensions: true);
        registry.RefreshExtensions();
        ShaderGraphCompileResult compilation = ShaderGraphCompiler.Compile(
            context.assetPath.ToString(),
            Path.GetFileNameWithoutExtension(context.assetPath.localPath),
            document,
            registry);
        output.SetAsset(asset);
        if (!compilation.succeeded || compilation.module is null)
            return;
        asset.CommitDefinition(compilation.module.definition);
        byte[] artifact = ShaderIRArtifactSerialization.Encode(compilation.module);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("shader-ir", artifact, cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        ShaderGraphAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.document is null)
            throw new InvalidOperationException("A Shader Graph asset requires a neutral graph document.");
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(asset));
    }
}
