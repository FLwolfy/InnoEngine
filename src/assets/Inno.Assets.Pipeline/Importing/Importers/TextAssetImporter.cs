using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Assets.Pipeline;

namespace Inno.Assets.Pipeline.Importers;

[AssetImporterExtension]
internal sealed class TextAssetImporter : AssetImporter<TextAsset>
{
    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } =
        [".txt", ".json", ".yaml", ".yml", ".md", ".xml"];

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
        AssetImportWriter<TextAsset> output,
        CancellationToken cancellationToken)
    {
        string content = context.ReadUtf8Text();
        string hint = context.extension switch
        {
            ".json" => "json",
            ".yaml" => "yaml",
            ".yml" => "yaml",
            ".xml" => "xml",
            ".md" => "markdown",
            _ => "plain"
        };

        var asset = new TextAsset(content, hint);
        output.SetAsset(asset);
        await output.WriteArtifactAsync(
            "runtime",
            Encoding.UTF8.GetBytes(content),
            cancellationToken).ConfigureAwait(false);
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
        TextAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(Encoding.UTF8.GetBytes(asset.content));
}
