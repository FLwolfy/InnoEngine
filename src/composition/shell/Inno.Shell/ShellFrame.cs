using System;

namespace Inno.Shell;

/// <summary>
/// Captures immutable timing and identity for one composition-shell frame.
/// </summary>
public readonly record struct ShellFrame
{
    /// <summary>
    /// Creates one validated shell frame.
    /// </summary>
    /// <param name="frameIndex">
    /// Zero-based shell frame index.
    /// </param>
    /// <param name="totalTime">
    /// Monotonic elapsed host time in seconds.
    /// </param>
    /// <param name="deltaTime">
    /// Non-negative elapsed time since the previous frame in seconds.
    /// </param>
    public ShellFrame(int frameIndex, double totalTime, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(totalTime);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaTime);
        this.frameIndex = frameIndex;
        this.totalTime = totalTime;
        this.deltaTime = deltaTime;
    }

    /// <summary>
    /// Gets the zero-based shell frame index.
    /// </summary>
    public int frameIndex { get; }

    /// <summary>
    /// Gets monotonic elapsed host time in seconds.
    /// </summary>
    public double totalTime { get; }

    /// <summary>
    /// Gets non-negative elapsed time since the previous frame in seconds.
    /// </summary>
    public float deltaTime { get; }
}
