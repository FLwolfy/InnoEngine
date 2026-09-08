using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Scene;

namespace Inno.Scene.Assets.Importers;

/// <summary>
/// Imports and exports <c>.iscene</c> source state.
/// </summary>
[AssetImporterExtension]
internal sealed class SceneAssetImporter : AssetImporter<SceneAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = new[] { ".iscene" };

    /// <summary>
    /// Gets the stable importer identity used in artifact fingerprints.
    /// </summary>
    public override string importerId => "inno.engine.scene";

    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    /// <summary>
    /// Imports source content into a validated runtime asset and artifact set.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
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
        AssetImportWriter<SceneAsset> output,
        CancellationToken cancellationToken)
    {
        EngineResourceEnvelope envelope = context.serialization.Deserialize<EngineResourceEnvelope>(
            context.sourceBytes.ToArray());
        envelope.Validate(EngineResourceEnvelope.C_SCENE_KIND);
        AssetDependency[] dependencies = envelope.dependencies;
        SceneAsset asset = SceneAsset.CreateImported(envelope.payload, dependencies);
        for (int i = 0; i < dependencies.Length; i++)
            output.DependsOnAsset(dependencies[i]);
        output.SetAsset(asset);
        await output.WriteArtifactAsync("runtime", envelope.payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a validated asset representation to its writable source mount.
    /// </summary>
    /// <param name="asset">
    /// The validated asset instance exported by this operation.
    /// </param>
    /// <param name="context">
    /// The generation-bound source export services.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        SceneAsset asset,
        CancellationToken cancellationToken)
    {
        EngineAssetContent content = asset.CaptureContent();
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(context.serialization.Serialize(
            new EngineResourceEnvelope
            {
                resourceKind = EngineResourceEnvelope.C_SCENE_KIND,
                payload = content.GetPayload(),
                dependencies = content.GetDependencies()
            }));
    }
}
