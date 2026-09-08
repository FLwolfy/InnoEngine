namespace Inno.Audio;

/// <summary>
/// Identifies a backend voice allocation, distinct from a service's preparing playback handle.
/// </summary>
public readonly record struct AudioDeviceVoiceHandle
{
    internal AudioDeviceVoiceHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    /// <summary>
    /// Gets the backend device generation that owns this voice.
    /// </summary>
    public uint deviceGeneration { get; }
    /// <summary>
    /// Gets whether this value identifies a backend allocation.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}
