using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;

namespace Inno.Scripting.Compiler;

[AssetImporterExtension]
internal sealed class ScriptAssemblyDefinitionImporter : AssetImporter<ScriptAssemblyDefinitionAsset>
{
    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.editor.script-assembly-definition";
    /// <summary>
    /// Gets whether imported output is deployed to runtime, editor, or both domains.
    /// </summary>
    public override AssetDeploymentScope deploymentScope => AssetDeploymentScope.AuthoringOnly;
    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".iasmdef"];

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
        AssetImportWriter<ScriptAssemblyDefinitionAsset> output,
        CancellationToken cancellationToken)
    {
        ScriptAssemblyDefinitionAsset asset = NativeAssetSourceSerialization.Import<ScriptAssemblyDefinitionAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        if (string.IsNullOrWhiteSpace(asset.assemblyName))
            throw new InvalidOperationException("Assembly definition name is required.");
        if (!Enum.IsDefined(asset.scope))
            throw new InvalidOperationException("Assembly definition scope must be Runtime or Editor.");
        output.SetAsset(asset);
        await output.WriteArtifactAsync("source", context.sourceBytes, cancellationToken)
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
        ScriptAssemblyDefinitionAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(asset.assemblyName))
            throw new InvalidOperationException("Assembly definition name is required.");
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }
}
