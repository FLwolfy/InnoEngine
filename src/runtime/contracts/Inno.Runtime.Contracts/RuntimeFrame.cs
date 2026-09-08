using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Provides immutable timing state to one variable runtime frame.
/// </summary>
public readonly record struct RuntimeFrame
{
    /// <summary>
    /// Creates the immutable timing snapshot for one variable frame.
    /// </summary>
    /// <param name="frameIndex">
    /// The monotonically increasing owner frame index.
    /// </param>
    /// <param name="time">
    /// Accumulated scaled time.
    /// </param>
    /// <param name="unscaledTime">
    /// Accumulated unscaled time.
    /// </param>
    /// <param name="deltaTime">
    /// Scaled interval.
    /// </param>
    /// <param name="unscaledDeltaTime">
    /// Unscaled interval.
    /// </param>
    /// <param name="timeScale">
    /// Simulation multiplier.
    /// </param>
    /// <param name="isPaused">
    /// Whether scaled simulation is paused.
    /// </param>
    public RuntimeFrame(
        long frameIndex,
        float time,
        float unscaledTime,
        float deltaTime,
        float unscaledDeltaTime,
        float timeScale,
        bool isPaused)
    {
        this.frameIndex = frameIndex;
        this.time = time;
        this.unscaledTime = unscaledTime;
        this.deltaTime = deltaTime;
        this.unscaledDeltaTime = unscaledDeltaTime;
        this.timeScale = timeScale;
        this.isPaused = isPaused;
    }

    /// <summary>
    /// Gets the zero-based session frame index.
    /// </summary>
    public long frameIndex { get; }

    /// <summary>
    /// Gets accumulated scaled session time in seconds.
    /// </summary>
    public float time { get; }

    /// <summary>
    /// Gets accumulated unscaled session time in seconds.
    /// </summary>
    public float unscaledTime { get; }

    /// <summary>
    /// Gets the scaled variable frame interval in seconds.
    /// </summary>
    public float deltaTime { get; }

    /// <summary>
    /// Gets the unscaled variable frame interval in seconds.
    /// </summary>
    public float unscaledDeltaTime { get; }

    /// <summary>
    /// Gets the simulation time multiplier used for this frame.
    /// </summary>
    public float timeScale { get; }

    /// <summary>
    /// Gets whether scaled simulation is paused for this frame.
    /// </summary>
    public bool isPaused { get; }
}
