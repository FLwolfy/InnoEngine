using System;

namespace Inno.Audio;

/// <summary>
/// Bounds device-side resource admission independently of the Runtime's smaller gameplay voice budget.
/// </summary>
public sealed class AudioDeviceLimits
{
    /// <summary>
    /// Creates immutable positive capacities for one backend generation.
    /// </summary>
    /// <param name="clips">
    /// Maximum simultaneously retained clip descriptors and native sources.
    /// </param>
    /// <param name="voices">
    /// Maximum active voices plus undelivered completions.
    /// </param>
    /// <param name="buses">
    /// Maximum simultaneously retained mixer buses.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A capacity is not positive.
    /// </exception>
    public AudioDeviceLimits(int clips = 16384, int voices = 65536, int buses = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clips);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(voices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buses);
        this.clips = clips;
        this.voices = voices;
        this.buses = buses;
    }

    /// <summary>
    /// Gets the maximum retained clip count.
    /// </summary>
    public int clips { get; }
    /// <summary>
    /// Gets the combined voice and undelivered completion capacity.
    /// </summary>
    public int voices { get; }
    /// <summary>
    /// Gets the maximum retained bus count.
    /// </summary>
    public int buses { get; }
}
