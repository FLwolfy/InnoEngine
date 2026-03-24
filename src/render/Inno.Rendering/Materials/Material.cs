
namespace Inno.Rendering;

/// <summary>
/// Represents base material state used by renderables.
/// </summary>
public abstract class Material
{
    public string name { get; set; } = string.Empty;

    public MaterialSurfaceType surfaceType { get; set; } = MaterialSurfaceType.Opaque;

    public MaterialBlendMode blendMode { get; set; } = MaterialBlendMode.Alpha;

    public MaterialCullMode cullMode { get; set; } = MaterialCullMode.Back;

    public MaterialDepthMode depthMode { get; set; } = MaterialDepthMode.ReadWrite;

    public bool castShadows { get; set; } = true;

    public bool receiveShadows { get; set; } = true;

    public MaterialKeywords keywords { get; } = new();

    public MaterialPropertyBlock overrides { get; } = new();
}
