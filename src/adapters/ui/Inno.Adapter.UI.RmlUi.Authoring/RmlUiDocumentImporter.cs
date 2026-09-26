using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;
using Inno.UI;
using Inno.UI.Assets;

namespace Inno.Adapter.UI.RmlUi.Authoring;

/// <summary>
/// Maps the conventional .rml extension to explicit RML and RmlUi identities.
/// </summary>
[AssetImporter("inno.ui.rml-document")]
public sealed class RmlUiDocumentImporter : AssetImporter<UiDocumentAsset>
{
    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".rml"];

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
    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<UiDocumentAsset> output,
        CancellationToken cancellationToken)
        => UiDocumentImportPipeline.ImportAsync(
            context,
            output,
            RmlUiIdentifiers.documentLanguage,
            RmlUiIdentifiers.backend.value,
            cancellationToken);
}
