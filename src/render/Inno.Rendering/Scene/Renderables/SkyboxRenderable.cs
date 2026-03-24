
namespace Inno.Rendering;

/// <summary>
/// Represents a skybox renderable object.
/// </summary>
public sealed class SkyboxRenderable : Renderable
{
    public required SkyboxMaterial material { get; init; }
}
