namespace Inno.Adapter.Audio;

/// <summary>
/// Configures backend-neutral audio device creation.
/// </summary>
public readonly struct AudioBackendOptions()
{
    /// <summary>
    /// Gets whether the device must run without opening an operating-system playback endpoint.
    /// </summary>
    public bool noDevice { get; init; }
}
