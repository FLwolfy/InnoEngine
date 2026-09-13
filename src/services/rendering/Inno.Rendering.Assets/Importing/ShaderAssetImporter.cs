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
        byte[] captured = ShaderGraphArtifact.Capture(graph, context.types, context.serialization, owner, (id, path) =>
        {
            context.DependsOnArtifact(id);
            AssetObject resolved = context.references.Resolve(id, context.services.GetStableTypeId<ShaderFunctionAsset>(),
                path, typeof(ShaderFunctionAsset), $"shader.sources[{id}]");
            if (resolved is not ShaderFunctionAsset { isMissing: false } source)
                throw new InvalidDataException($"Shader function '{id}' is unavailable. Its graph reference is preserved.");
            using ArtifactLease sourceLease = context.AcquireArtifact(source.identity.persistentId, ShaderSourceBundle.outputName);
            return File.ReadAllBytes(sourceLease.info.absolutePath);
        }, cancellationToken);
        ShaderDefinition definition = ShaderGraphArtifact.ReadDefinition(captured, context.serialization, owner);
        // Function dependencies are authoring-only; texture defaults remain ordinary runtime references.
        _ = context.serialization.Serialize(definition, owner);
        foreach (AssetDependency dependency in dependencies.dependencies) output.DependsOnAsset(dependency);
        var asset = new ShaderAsset();
        asset.SetDefinition(definition, context.serialization, owner);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        await output.WriteArtifactAsync(ShaderGraphArtifact.outputName, captured,
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
