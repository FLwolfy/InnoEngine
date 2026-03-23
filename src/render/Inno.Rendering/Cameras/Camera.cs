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
        var target = transform.position + transform.forward;
        return Matrix.CreateLookAtRH(transform.position, target, transform.up);
    }

    public abstract Matrix GetProjectionMatrix(float aspectRatio);
}

/// <summary>
/// Represents a perspective camera.
/// </summary>
public sealed class PerspectiveCamera : Camera
{
    public float fieldOfViewDegrees { get; set; } = 60.0f;

    public override Matrix GetProjectionMatrix(float aspectRatio)
    {
        var fovRadians = MathF.PI / 180.0f * fieldOfViewDegrees;
        return Matrix.CreatePerspectiveFieldOfViewRH(fovRadians, aspectRatio, nearClip, farClip);
    }
}

/// <summary>
/// Represents an orthographic camera.
/// </summary>
public sealed class OrthographicCamera : Camera
{
    public float orthoHeight { get; set; } = 10.0f;

    public override Matrix GetProjectionMatrix(float aspectRatio)
    {
        var width = orthoHeight * aspectRatio;
        return Matrix.CreateOrthographic(width, orthoHeight, nearClip, farClip);
    }
}
