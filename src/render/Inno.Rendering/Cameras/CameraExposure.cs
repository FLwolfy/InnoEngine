using Inno.Core.Mathematics;

namespace Inno.Rendering;

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
