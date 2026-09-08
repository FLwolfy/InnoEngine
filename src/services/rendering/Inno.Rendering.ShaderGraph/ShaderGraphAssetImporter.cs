using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Rendering.Assets;

namespace Inno.Rendering.ShaderGraph;

[AssetImporterExtension]
internal sealed class ShaderGraphAssetImporter : AssetImporter<ShaderGraphAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.shader-graph";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishadergraph"];

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
        AssetImportWriter<ShaderGraphAsset> output,
        CancellationToken cancellationToken)
    {
        ShaderGraphAsset asset = NativeAssetSourceSerialization.Import<ShaderGraphAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        GraphDocument document = asset.document
            ?? throw new InvalidDataException("A Shader Graph asset requires a neutral graph document.");
        using var registry = new ShaderNodeRegistry(context.types);
        registry.RefreshExtensions();
        ShaderGraphCompileResult compilation = ShaderGraphCompiler.Compile(
            context.assetPath.ToString(),
            Path.GetFileNameWithoutExtension(context.assetPath.localPath),
            document,
            registry,
            context.serialization);
        output.SetAsset(asset);
        if (!compilation.succeeded || compilation.module is null)
            return;
        asset.CommitDefinition(compilation.module.definition, context.serialization);
        byte[] artifact = ShaderIRArtifactSerialization.Encode(compilation.module, context.serialization);
        await output.WriteArtifactAsync("runtime", artifact, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync("shader-ir", artifact, cancellationToken).ConfigureAwait(false);
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
        ShaderGraphAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.document is null)
            throw new InvalidOperationException("A Shader Graph asset requires a neutral graph document.");
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }
}
