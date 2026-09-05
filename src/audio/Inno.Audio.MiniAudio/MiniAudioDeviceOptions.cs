using System;

namespace Inno.Audio.MiniAudio;

/// <summary>
/// Configures creation of one MiniAudio backend generation.
/// </summary>
public sealed record MiniAudioDeviceOptions
{
    /// <summary>
    /// Gets or initializes whether the engine advances without opening an operating-system output device.
    /// </summary>
    public bool noDevice { get; init; }

    /// <summary>
    /// Gets or initializes the output channel count used by the engine graph.
    /// </summary>
    public int channels { get; init; } = 2;

    /// <summary>
    /// Gets or initializes the output sample rate in frames per second.
    /// </summary>
    public int sampleRate { get; init; } = 48000;

    /// <summary>
    /// Gets or initializes the maximum listener count exposed by this backend generation.
    /// </summary>
    public int listenerCount { get; init; } = 4;

    internal MiniAudioDeviceOptions Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (listenerCount is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(listenerCount), "MiniAudio supports between one and four listeners.");
        return this;
    }
}
