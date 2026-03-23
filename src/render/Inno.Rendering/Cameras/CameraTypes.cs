using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents camera transform data.
/// </summary>
public struct CameraTransform
{
    public Vector3 position { get; set; }

    public Quaternion rotation { get; set; }

    public Vector3 forward => Vector3.Transform(Vector3.BACK, rotation);

    public Vector3 up => Vector3.Transform(Vector3.UP, rotation);
}

/// <summary>
/// Represents camera exposure controls.
/// </summary>
public struct CameraExposure
{
    public float exposureCompensation { get; set; }

    public float aperture { get; set; }

    public float shutterSpeed { get; set; }

    public float iso { get; set; }

    public static CameraExposure @default => new()
    {
        exposureCompensation = 0.0f,
        aperture = 16.0f,
        shutterSpeed = 1.0f / 125.0f,
        iso = 100.0f
    };
}
