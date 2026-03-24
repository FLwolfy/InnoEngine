
namespace Inno.Rendering;

/// <summary>
/// Represents mesh-based renderable object.
/// </summary>
public sealed class MeshRenderable : Renderable
{
    public required Mesh mesh { get; init; }

    public required Material material { get; init; }

    public MaterialPropertyBlock? materialOverrides { get; set; }
}
