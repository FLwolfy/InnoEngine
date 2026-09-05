using System;

namespace Inno.Audio;

/// <summary>
/// Identifies a mixer bus without imposing a closed bus catalog.
/// </summary>
public readonly record struct AudioBusId
{
    /// <summary>
    /// Creates an open mixer bus identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable bus protocol value.
    /// </param>
    public AudioBusId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the mandatory root bus identifier.
    /// </summary>
    public static AudioBusId master { get; } = new("inno.audio.bus.master");

    /// <summary>
    /// Gets the globally stable protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable protocol value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the identifier for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The stable protocol value.
    /// </returns>
    public override string ToString() => value;
}

/// <summary>
/// Identifies an audio processor implementation without imposing a closed effect catalog.
/// </summary>
public readonly record struct AudioProcessorId
{
    /// <summary>
    /// Creates an open processor identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable processor protocol value.
    /// </param>
    public AudioProcessorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the standard low-pass filter processor identifier.
    /// </summary>
    public static AudioProcessorId lowPass { get; } = new("inno.audio.processor.low-pass");

    /// <summary>
    /// Gets the standard high-pass filter processor identifier.
    /// </summary>
    public static AudioProcessorId highPass { get; } = new("inno.audio.processor.high-pass");

    /// <summary>
    /// Gets the standard band-pass filter processor identifier.
    /// </summary>
    public static AudioProcessorId bandPass { get; } = new("inno.audio.processor.band-pass");

    /// <summary>
    /// Gets the standard notch filter processor identifier.
    /// </summary>
    public static AudioProcessorId notch { get; } = new("inno.audio.processor.notch");

    /// <summary>
    /// Gets the standard peak equalizer processor identifier.
    /// </summary>
    public static AudioProcessorId peak { get; } = new("inno.audio.processor.peak");

    /// <summary>
    /// Gets the standard low-shelf equalizer processor identifier.
    /// </summary>
    public static AudioProcessorId lowShelf { get; } = new("inno.audio.processor.low-shelf");

    /// <summary>
    /// Gets the standard high-shelf equalizer processor identifier.
    /// </summary>
    public static AudioProcessorId highShelf { get; } = new("inno.audio.processor.high-shelf");

    /// <summary>
    /// Gets the standard delay processor identifier.
    /// </summary>
    public static AudioProcessorId delay { get; } = new("inno.audio.processor.delay");

    /// <summary>
    /// Gets the globally stable protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable protocol value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the identifier for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The stable protocol value.
    /// </returns>
    public override string ToString() => value;
}

/// <summary>
/// Identifies one processor parameter without imposing a processor-specific enum.
/// </summary>
public readonly record struct AudioParameterId
{
    /// <summary>
    /// Creates an open processor parameter identifier.
    /// </summary>
    /// <param name="value">
    /// Stable parameter name within its processor protocol.
    /// </param>
    public AudioParameterId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the standard cutoff or center-frequency parameter in hertz.
    /// </summary>
    public static AudioParameterId frequency { get; } = new("inno.audio.parameter.frequency-hz");

    /// <summary>
    /// Gets the standard filter quality-factor parameter.
    /// </summary>
    public static AudioParameterId quality { get; } = new("inno.audio.parameter.quality");

    /// <summary>
    /// Gets the standard equalizer gain parameter in decibels.
    /// </summary>
    public static AudioParameterId gainDecibels { get; } = new("inno.audio.parameter.gain-db");

    /// <summary>
    /// Gets the standard equalizer shelf-slope parameter.
    /// </summary>
    public static AudioParameterId shelfSlope { get; } = new("inno.audio.parameter.shelf-slope");

    /// <summary>
    /// Gets the standard delay duration parameter in milliseconds.
    /// </summary>
    public static AudioParameterId delayMilliseconds { get; } = new("inno.audio.parameter.delay-ms");

    /// <summary>
    /// Gets the standard delay feedback decay factor.
    /// </summary>
    public static AudioParameterId decay { get; } = new("inno.audio.parameter.decay");

    /// <summary>
    /// Gets the stable parameter protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable protocol value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the identifier for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The stable protocol value.
    /// </returns>
    public override string ToString() => value;
}

/// <summary>
/// Identifies encoded audio data without constraining codec providers to a closed enum.
/// </summary>
public readonly record struct AudioCodecId
{
    /// <summary>
    /// Creates an open codec identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable codec protocol value.
    /// </param>
    public AudioCodecId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the standard Waveform Audio codec identifier.
    /// </summary>
    public static AudioCodecId wav { get; } = new("audio/wav");

    /// <summary>
    /// Gets the standard Free Lossless Audio Codec identifier.
    /// </summary>
    public static AudioCodecId flac { get; } = new("audio/flac");

    /// <summary>
    /// Gets the standard MPEG Layer III codec identifier.
    /// </summary>
    public static AudioCodecId mp3 { get; } = new("audio/mpeg");

    /// <summary>
    /// Gets the globally stable protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable protocol value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the identifier for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The stable protocol value.
    /// </returns>
    public override string ToString() => value;
}
