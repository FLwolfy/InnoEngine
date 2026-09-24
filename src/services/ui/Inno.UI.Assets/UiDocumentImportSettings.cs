using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.UI.Assets;

/// <summary>Explicitly selects language and runtime implementation for neutral UI source.</summary>
[StableTypeId("7a56cdb8-eec0-49fd-bdd5-659d24c091b5")]
public sealed class UiDocumentImportSettings : ISerializable
{
    /// <summary>Gets or sets the registered source-language identity.</summary>
    [SerializableProperty] public string languageId { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact runtime implementation identity.</summary>
    [SerializableProperty] public string implementationId { get; set; } = string.Empty;
}
