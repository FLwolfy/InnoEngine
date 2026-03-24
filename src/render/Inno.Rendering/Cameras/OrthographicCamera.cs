using Inno.Core.Mathematics;

namespace Inno.Rendering;

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
