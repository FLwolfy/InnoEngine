using Inno.Runtime.Contracts;
using Inno.Core.Execution;
using System;
using System.Threading;

namespace Inno.Runtime;

/// <summary>
/// Provides Unity-style timing values for the runtime session bound to the current execution context.
/// </summary>
/// <remarks>
/// This façade owns no process-global timing state. Engine systems update an instance clock and scripts resolve
/// that clock through the active <see cref="RuntimeSession"/> scope.
/// </remarks>
public static class Time
{
    /// <summary>
    /// Gets the total elapsed session time in seconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float time => RuntimeClock.current.time;

    /// <summary>
    /// Gets the current variable frame interval in seconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float deltaTime => RuntimeClock.current.deltaTime;

    /// <summary>
    /// Gets total elapsed session time unaffected by pause or time scaling.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float unscaledTime => RuntimeClock.current.unscaledTime;

    /// <summary>
    /// Gets the current frame interval unaffected by pause or time scaling.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float unscaledDeltaTime => RuntimeClock.current.unscaledDeltaTime;

    /// <summary>
    /// Gets the accumulated fixed simulation time in seconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float fixedTime => RuntimeClock.current.fixedTime;

    /// <summary>
    /// Gets the interval of the active fixed simulation step in seconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float fixedDeltaTime => RuntimeClock.current.fixedDeltaTime;

    /// <summary>
    /// Gets the current simulation time multiplier.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static float timeScale
    {
        get => RuntimeClock.current.timeScale;
        set => RuntimeClock.current.SetTimeScale(value);
    }

    /// <summary>
    /// Gets whether scaled simulation is paused.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static bool isPaused
    {
        get => RuntimeClock.current.isPaused;
        set => RuntimeClock.current.SetPaused(value);
    }

    /// <summary>
    /// Gets the number of variable frames begun by this session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no runtime session is bound to the current execution context.
    /// </exception>
    public static long frameCount => RuntimeClock.current.frameCount;
}

internal sealed class RuntimeClock
{
    private static readonly ExecutionSlot<RuntimeClock> S_CURRENT_SCOPE = new("clock");

    internal static RuntimeClock current
        => S_CURRENT_SCOPE.current;

    internal float time { get; private set; }

    internal float deltaTime { get; private set; }

    internal float unscaledTime { get; private set; }

    internal float unscaledDeltaTime { get; private set; }

    internal float fixedTime { get; private set; }

    internal float fixedDeltaTime { get; private set; }

    internal float timeScale { get; private set; } = 1f;

    internal bool isPaused { get; private set; }

    internal long frameCount { get; private set; }

    internal long fixedStepCount { get; private set; }

    internal IDisposable EnterScope()
    {
        return S_CURRENT_SCOPE.Enter(this);
    }

    internal RuntimeFrame Update(float unscaledDeltaTime)
    {
        this.unscaledDeltaTime = unscaledDeltaTime;
        unscaledTime += unscaledDeltaTime;
        deltaTime = isPaused ? 0f : unscaledDeltaTime * timeScale;
        time += deltaTime;
        var frame = new RuntimeFrame(
            frameCount,
            time,
            unscaledTime,
            deltaTime,
            unscaledDeltaTime,
            timeScale,
            isPaused);
        frameCount++;
        return frame;
    }

    internal RuntimeFixedFrame BeginFixedStep(float fixedDeltaTime)
    {
        this.fixedDeltaTime = fixedDeltaTime;
        fixedTime += fixedDeltaTime;
        var frame = new RuntimeFixedFrame(fixedStepCount, fixedTime, fixedDeltaTime);
        fixedStepCount++;
        return frame;
    }

    internal void SetTimeScale(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(nameof(value), "Time scale must be finite and non-negative.");
        timeScale = value;
    }

    internal void SetPaused(bool value) => isPaused = value;

}
