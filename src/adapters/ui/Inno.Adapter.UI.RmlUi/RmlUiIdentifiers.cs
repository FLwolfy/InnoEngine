using Inno.Adapter.UI;
using Inno.UI;

namespace Inno.Adapter.UI.RmlUi;

/// <summary>
/// Publishes stable identities owned by the bundled RmlUi adapter.
/// </summary>
public static class RmlUiIdentifiers
{
    /// <summary>
    /// Gets the runtime implementation identity.
    /// </summary>
    public static UiBackendId backend { get; } = UiBackendId.rmlUi;

    /// <summary>
    /// Gets the RML source-language identity.
    /// </summary>
    public static UiDocumentLanguageId documentLanguage { get; } = new("inno.ui-language.rml");
}
