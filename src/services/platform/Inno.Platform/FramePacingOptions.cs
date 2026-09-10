using System;

namespace Inno.Platform;

/// <summary>
/// Defines host presentation cadence independently of simulation and fixed-step timing.
/// </summary>
public sealed class FramePacingOptions
{
    private int m_maximumFrameRate;

    /// <summary>
    /// Gets or sets whether presentation waits for display synchronization.
    /// </summary>
    public bool verticalSync { get; set; }

    /// <summary>
    /// Gets or sets the software frame-rate ceiling. Zero leaves the frame rate unlimited.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int maximumFrameRate
    {
        get => m_maximumFrameRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            m_maximumFrameRate = value;
        }
    }
}
