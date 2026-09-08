namespace Inno.Runtime.Contracts;

/// <summary>
/// Provides immutable timing state to one deterministic fixed simulation step.
/// </summary>
public readonly record struct RuntimeFixedFrame
{
    /// <summary>
    /// Creates one deterministic fixed-step timing value.
    /// </summary>
    /// <param name="stepIndex">
    /// The monotonically increasing fixed-step index.
    /// </param>
    /// <param name="time">
    /// Accumulated scaled fixed time.
    /// </param>
    /// <param name="deltaTime">
    /// The fixed interval.
    /// </param>
    public RuntimeFixedFrame(long stepIndex, float time, float deltaTime)
    {
        this.stepIndex = stepIndex;
        this.time = time;
        this.deltaTime = deltaTime;
    }

    /// <summary>
    /// Gets the zero-based fixed-step index.
    /// </summary>
    public long stepIndex { get; }

    /// <summary>
    /// Gets accumulated fixed simulation time in seconds.
    /// </summary>
    public float time { get; }

    /// <summary>
    /// Gets the configured fixed simulation interval in seconds.
    /// </summary>
    public float deltaTime { get; }
}
