using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Rendering.Assets;

[AssetImporterExtension]
internal sealed class ShaderAssetImporter : AssetImporter<ShaderAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.rendering.shader";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ishader"];

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
        AssetImportWriter<ShaderAsset> output,
        CancellationToken cancellationToken)
    {
        ShaderAsset asset = NativeAssetSourceSerialization.Import<ShaderAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
        {
            output.DependsOnAsset(dependency);
            output.DependsOnArtifact(dependency.persistentId);
        }
        ShaderIRModule module = CreateModule(asset, context.assetPath.ToString());
        ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);
        ShaderDiagnostic? error = validation.diagnostics.FirstOrDefault(static diagnostic =>
            diagnostic.severity == ShaderDiagnosticSeverity.Error);
        if (error is not null)
            throw new InvalidOperationException(error.message);
        foreach (ShaderDiagnostic diagnostic in validation.diagnostics)
            output.ReportDiagnostic(diagnostic.message);

        output.SetAsset(asset);
        byte[] artifact = ShaderIRArtifactSerialization.Encode(module, context.serialization);
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
        ShaderAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = CreateModule(asset, asset.assetPath.ToString().Length == 0 ? "unsaved.ishader" : asset.assetPath.ToString());
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }

    private static ShaderIRModule CreateModule(ShaderAsset asset, string assetPath)
    {
        ShaderDefinition definition = asset.definition
            ?? throw new InvalidOperationException("A shader asset requires a committed definition.");
        ShaderIRPass[] passes = definition.passes.Select(pass =>
        {
            var stages = new List<ShaderIRStageModule>();
            switch (pass.programKind)
            {
                case ShaderProgramKind.Raster:
                    AddStage(pass.vertexSource, ShaderStage.Vertex);
                    AddStage(pass.fragmentSource, ShaderStage.Fragment);
                    if (pass.computeSource is not null)
                        throw new InvalidOperationException($"Raster pass '{pass.name}' cannot declare compute source.");
                    break;
                case ShaderProgramKind.Compute:
                    AddStage(pass.computeSource, ShaderStage.Compute);
                    if (pass.vertexSource is not null || pass.fragmentSource is not null || pass.varyingSource is not null)
                        throw new InvalidOperationException($"Compute pass '{pass.name}' cannot declare raster sources.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pass.programKind));
            }
            return new ShaderIRPass(pass, stages, pass.varyingSource?.content);

            void AddStage(ShaderSourceAsset? source, ShaderStage stage)
            {
                if (source is null || string.IsNullOrWhiteSpace(source.content))
                    throw new InvalidOperationException($"Pass '{pass.name}' requires non-empty {stage} source.");
                stages.Add(new ShaderIRStageModule(
                    stage,
                    "main",
                    source.content,
                    ShaderIRSourceKind.Handwritten,
                    new ShaderSourceLocation(
                        string.IsNullOrWhiteSpace(source.assetPath.localPath)
                            ? assetPath
                            : source.assetPath.ToString(),
                        pass.name,
                        stage)));
            }
        }).ToArray();
        return new ShaderIRModule(definition, passes);
    }
}
