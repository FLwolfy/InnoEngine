
namespace Inno.Rendering;

/// <summary>
/// Represents screen-space or world-space sprite renderable.
/// </summary>
public sealed class SpriteRenderable : Renderable
{
    public required SpriteMaterial material { get; init; }
}
