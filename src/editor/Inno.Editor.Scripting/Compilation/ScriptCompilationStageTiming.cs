using System;

namespace Inno.Editor.Scripting;

/// <summary>
/// Describes the elapsed wall time of one completed script compilation stage.
/// </summary>
public readonly record struct ScriptCompilationStageTiming
{
    /// <summary>
    /// Creates one immutable stage timing sample.
    /// </summary>
    /// <param name="stage">The human-readable stage description.</param>
    /// <param name="elapsed">The wall time spent in the stage.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="stage"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="elapsed"/> is negative.
    /// </exception>
    public ScriptCompilationStageTiming(string stage, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(stage))
            throw new ArgumentException("A compilation stage name is required.", nameof(stage));
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Stage duration cannot be negative.");
        this.stage = stage;
        this.elapsed = elapsed;
    }

    /// <summary>Gets the human-readable stage description.</summary>
    public string stage { get; }

    /// <summary>Gets the wall time spent in the stage.</summary>
    public TimeSpan elapsed { get; }
}
