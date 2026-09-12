using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Assets;

/// <summary>Configures one callable source function through the standard asset import-settings sidecar.</summary>
[StableTypeId("59c92a59-ef51-4587-b917-df5d187d2717")]
public sealed class ShaderSourceImportSettings : ISerializable
{
    /// <summary>Gets or sets the registered language identity; an empty selection is an import error.</summary>
    [SerializableProperty] public string languageId { get; set; } = string.Empty;
    /// <summary>Gets or sets the sole public function exposed as graph ports.</summary>
    [SerializableProperty] public string entryPoint { get; set; } = "Evaluate";
    /// <summary>Gets or sets the exact implementation key used by the selected rendering backend.</summary>
    [SerializableProperty] public string implementationId { get; set; } = string.Empty;
    /// <summary>Gets or sets alternate source implementations; all must expose the same public interface with unique backend IDs.</summary>
    [SerializableProperty] public ShaderFunctionAsset[] implementations { get; set; } = [];
}
