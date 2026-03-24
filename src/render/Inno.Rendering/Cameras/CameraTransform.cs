using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents camera transform data.
/// </summary>
public struct CameraTransform
{
    public Vector3 position { get; set; }

    public Quaternion rotation { get; set; }

    public Vector3 forward => Vector3.Transform(Vector3.FORWARD, rotation);

    public Vector3 up => Vector3.Transform(Vector3.UP, rotation);
}
