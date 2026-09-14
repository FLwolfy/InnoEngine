using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Assets;

/// <summary>Represents an authoring-only source-function library; it is never a complete GPU stage or runtime dependency.</summary>
[StableTypeId("d8b5f6ec-c883-43b9-a5cd-95e7e24d6bba")]
public sealed class ShaderFunctionAsset : AssetObject
{
    /// <summary>Gets the configured source language identity.</summary>
    [SerializableProperty] public string languageId { get; internal set; } = string.Empty;
    /// <summary>Gets the explicitly exported functions available to graph source-function nodes.</summary>
    [SerializableProperty] public string[] exports { get; internal set; } = [];
    /// <summary>Gets the adapter implementation identity.</summary>
    [SerializableProperty] public string implementationId { get; internal set; } = string.Empty;
    /// <summary>Gets the author-declared catalog path used by Shader creation tools.</summary>
    [SerializableProperty] public string catalogPath { get; internal set; } = string.Empty;
    /// <summary>Gets the stable ordering value inside the authoring catalog.</summary>
    [SerializableProperty] public int catalogOrder { get; internal set; }
}
