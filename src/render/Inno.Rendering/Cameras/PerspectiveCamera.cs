using Inno.Core.Mathematics;

namespace Inno.Rendering;

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
