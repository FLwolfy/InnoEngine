namespace Inno.Audio;

/// <summary>
/// Identifies one backend clip allocation within a device generation.
/// </summary>
public readonly record struct AudioClipHandle
{
    internal AudioClipHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }

    /// <summary>
    /// Gets the generation of the device that created this handle.
    /// </summary>
    public uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle contains a non-zero backend identity and generation.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies one playback voice within a device generation.
/// </summary>
public readonly record struct AudioVoiceHandle
{
    internal AudioVoiceHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }

    /// <summary>
    /// Gets the generation of the device that created this handle.
    /// </summary>
    public uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle contains a non-zero backend identity and generation.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies one backend mixer bus within a device generation.
/// </summary>
public readonly record struct AudioBusHandle
{
    internal AudioBusHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }

    /// <summary>
    /// Gets the generation of the device that created this handle.
    /// </summary>
    public uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle contains a non-zero backend identity and generation.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies one spatial listener within a device generation.
/// </summary>
public readonly record struct AudioListenerHandle
{
    internal AudioListenerHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }

    /// <summary>
    /// Gets the generation of the device that created this handle.
    /// </summary>
    public uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle contains a non-zero backend identity and generation.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}
