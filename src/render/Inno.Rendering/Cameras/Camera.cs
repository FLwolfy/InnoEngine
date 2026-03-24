using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a view camera definition.
/// </summary>
public abstract class Camera
{
    public CameraTransform transform { get; set; } = new()
    {
        position = Vector3.ZERO,
        rotation = Quaternion.identity
    };

    public float nearClip { get; set; } = 0.1f;

    public float farClip { get; set; } = 1000.0f;

    public CameraExposure exposure { get; set; } = CameraExposure.@default;

    public virtual Matrix GetViewMatrix()
    {
        var rotation = Matrix.CreateFromQuaternion(transform.rotation);
        var translation = Matrix.CreateTranslation(transform.position);
        var world = translation * rotation;
        return Matrix.Invert(world);
    }

    public abstract Matrix GetProjectionMatrix(float aspectRatio);
}
