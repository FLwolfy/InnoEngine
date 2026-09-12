using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class ShaderAssetImporter : AssetImporter<ShaderAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.shader";
    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishader"];

    /// <inheritdoc />
    protected override async ValueTask ImportAsync(AssetImportContext context, AssetImportWriter<ShaderAsset> output,
        CancellationToken cancellationToken)
    {
        GraphDocument graph = GraphDocumentCodec.Decode(context.sourceBytes.Span, context.serialization);
        var dependencies = new AssetDependencyCollection();
        SerializationContext owner = AssetSerializationContext.Create(context.references, dependencies);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, context.serialization, owner);
        var sources = new Dictionary<GraphNodeId, byte[]>();
        foreach (GraphNodeRecord node in graph.nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.definitionId != "inno.shader.source") continue;
            Guid id = ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, context.serialization, owner);
            if (id == Guid.Empty) continue;
            context.DependsOnArtifact(id);
            string path = ShaderGraphDocument.Read(node, "sourcePath", "", context.serialization, owner);
            AssetObject resolved = context.references.Resolve(id, context.services.GetStableTypeId<ShaderFunctionAsset>(),
                path, typeof(ShaderFunctionAsset), $"shader.nodes[{node.id}].sourceId");
            if (resolved is not ShaderFunctionAsset { isMissing: false } source)
                throw new InvalidDataException($"Shader function '{id}' is unavailable. Its graph reference is preserved.");
            using ArtifactLease sourceLease = context.AcquireArtifact(source.identity.persistentId, ShaderSourceBundle.outputName);
            sources.Add(node.id, File.ReadAllBytes(sourceLease.info.absolutePath));
        }
        // Function dependencies are authoring-only; texture defaults remain ordinary runtime references.
        _ = context.serialization.Serialize(definition, owner);
        foreach (AssetDependency dependency in dependencies.dependencies) output.DependsOnAsset(dependency);
        var asset = new ShaderAsset();
        asset.SetDefinition(definition, context.serialization, owner);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync(ShaderGraphArtifact.outputName, ShaderGraphArtifact.Encode(graph, sources, context.serialization),
            cancellationToken, AssetDeploymentScope.AuthoringOnly).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(AssetExportContext context, ShaderAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(GraphDocumentCodec.Encode(
            ShaderGraphArtifact.ReadDocument(ShaderGraphArtifact.Read(asset, context.artifacts), context.serialization), context.serialization));
    }
}
