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
}

internal sealed class RuntimeClock
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    internal static RuntimeClock current
        => S_CURRENT_SCOPE.Value?.clock
            ?? throw new InvalidOperationException(
                "No runtime clock is bound to the current execution context.");

    internal float time { get; private set; }

    internal float deltaTime { get; private set; }

    internal float fixedTime { get; private set; }

    internal float fixedDeltaTime { get; private set; }

    internal IDisposable EnterScope()
    {
        var scope = new Scope(this, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    internal void Update(float totalTime, float deltaTime)
    {
        time = totalTime;
        this.deltaTime = deltaTime;
    }

    internal void BeginFixedStep(float fixedDeltaTime)
    {
        this.fixedDeltaTime = fixedDeltaTime;
        fixedTime += fixedDeltaTime;
    }

    private sealed class Scope(RuntimeClock clock, Scope? previous) : IDisposable
    {
        private bool m_disposed;

        internal RuntimeClock clock { get; } = clock;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
            {
                throw new InvalidOperationException(
                    "Runtime clock execution scopes must be disposed in last-in-first-out order.");
            }
            m_disposed = true;
            S_CURRENT_SCOPE.Value = previous;
        }
    }
}
