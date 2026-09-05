using System;

namespace Inno.Audio.Runtime;

/// <summary>
/// Configures bounded audio runtime resource policies.
/// </summary>
public sealed class AudioRuntimeOptions
{
    /// <summary>
    /// Gets or sets the maximum number of preparing, scheduled, paused, and playing voices.
    /// </summary>
    public int maxVoices { get; set; } = 128;

    /// <summary>
    /// Gets or sets the decoded clip cache budget in bytes.
    /// </summary>
    public long decodedCacheBudgetBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the encoded byte threshold used by automatic load-mode selection.
    /// </summary>
    public long automaticStreamingThresholdBytes { get; set; } = 2L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the positive delay between output-device recovery attempts in seconds.
    /// </summary>
    public float deviceRecoveryIntervalSeconds { get; set; } = 2f;

    internal AudioRuntimeOptions Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVoices);
        ArgumentOutOfRangeException.ThrowIfNegative(decodedCacheBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(automaticStreamingThresholdBytes);
        if (!float.IsFinite(deviceRecoveryIntervalSeconds) || deviceRecoveryIntervalSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deviceRecoveryIntervalSeconds));
        return this;
    }
}
