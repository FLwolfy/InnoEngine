using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents position, rotation, and scale transform data.
/// </summary>
public struct Transform
{
    public Vector3 position { get; set; }

    public Quaternion rotation { get; set; }

    public Vector3 scale { get; set; }

    public Matrix ToMatrix()
    {
        var scale = Matrix.CreateScale(this.scale);
        var rotation = Matrix.CreateFromQuaternion(this.rotation);
        var translation = Matrix.CreateTranslation(this.position);
        return scale * rotation * translation;
    }

    public static Transform identity => new()
    {
        position = Vector3.ZERO,
        rotation = Quaternion.identity,
        scale = Vector3.ONE
    };
}
