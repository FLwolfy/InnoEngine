using System;

using Inno.UI;
using Inno.UI.Assets;

namespace Inno.Adapter.UI.RmlUi.Authoring;

/// <summary>Validates RML source for the bundled RmlUi runtime adapter.</summary>
public sealed class RmlUiDocumentFrontend : IUiDocumentFrontend
{
    /// <inheritdoc />
    public UiDocumentLanguageId languageId => RmlUiIdentifiers.documentLanguage;

    /// <inheritdoc />
    public UiDocumentAnalysis Analyze(UiDocumentSourceFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.text))
            return new(null, [new("RML_EMPTY", "The RML document is empty.", 1, 1)]);
        if (!source.text.Contains("<rml", StringComparison.OrdinalIgnoreCase))
            return new(null, [new("RML_ROOT_MISSING", "The RML document has no root rml element.", 1, 1)]);
        return new(source.text, []);
    }
}
