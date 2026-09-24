using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;
using Inno.UI;
using Inno.UI.Assets;

namespace Inno.Adapter.UI.RmlUi.Authoring;

/// <summary>Maps the conventional .rml extension to explicit RML and RmlUi identities.</summary>
[AssetImporter("inno.ui.rml-document")]
public sealed class RmlUiDocumentImporter : AssetImporter<UiDocumentAsset>
{
    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".rml"];

    /// <inheritdoc />
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
