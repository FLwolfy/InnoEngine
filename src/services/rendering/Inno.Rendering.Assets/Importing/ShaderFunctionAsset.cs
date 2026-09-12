using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Assets;

/// <summary>Represents a callable authoring-only source module; it is never a complete GPU stage or runtime dependency.</summary>
[StableTypeId("d8b5f6ec-c883-43b9-a5cd-95e7e24d6bba")]
public sealed class ShaderFunctionAsset : AssetObject
{
    /// <summary>Gets the configured source language identity.</summary>
    [SerializableProperty] public string languageId { get; internal set; } = string.Empty;
    /// <summary>Gets the selected public function.</summary>
    [SerializableProperty] public string entryPoint { get; internal set; } = string.Empty;
    /// <summary>Gets the adapter implementation identity.</summary>
    [SerializableProperty] public string implementationId { get; internal set; } = string.Empty;
}
