using System;
namespace Inno.Scripting.Reload;

/// <summary>
/// Configures automatic compilation requests and retired-generation verification.
/// </summary>
public sealed class ScriptReloadOptions
{
    /// <summary>
    /// Gets whether startup and subsequent source changes request automatic compilation.
    /// </summary>
    /// <remarks>
    /// The initial request starts immediately. Later change requests are consumed only at a focused
    /// Editor safe point after the configured debounce duration.
    /// </remarks>
    public bool autoCompile { get; init; } = true;

    /// <summary>
    /// Gets the quiet period applied to source changes before a compilation starts, in milliseconds.
    /// </summary>
    public int debounceMilliseconds { get; init; } = 250;

    /// <summary>
    /// Gets the elapsed duration after which an active compilation is reported as long-running.
    /// </summary>
    /// <remarks>
    /// Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the warning. The warning
    /// never cancels compilation automatically.
    /// </remarks>
    public TimeSpan compilationWarningTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
