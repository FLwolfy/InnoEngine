namespace Inno.Core.Framework;

/// <summary>
/// Runtime settings for <see cref="Shell"/>.
/// </summary>
public readonly struct ShellSettings
{
    /// <summary>
    /// Creates shell settings with default values.
    /// </summary>
    public ShellSettings()
    {
        fixedDeltaTime = 1f / 60f;
        useBackgroundRenderThread = false;
        maxFrameRate = 0;
    }

    /// <summary>
    /// Fixed simulation timestep in seconds.
    /// </summary>
    public float fixedDeltaTime { get; init; }

    /// <summary>
    /// Whether to run rendering on a dedicated background thread using fully async submission.
    /// </summary>
    public bool useBackgroundRenderThread { get; init; }

    /// <summary>
    /// Maximum main-loop frame rate. Set to 0 to disable frame limiting.
    /// </summary>
    public int maxFrameRate { get; init; }
}
