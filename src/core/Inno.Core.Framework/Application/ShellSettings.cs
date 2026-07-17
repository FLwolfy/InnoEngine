using System.IO;

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
        maxFrameDeltaTime = 0.25f;
        maxUpdateStepsPerTick = 8;
        useSingleThreadJobSystem = false;
        jobWorkerCount = 0;
        projectRootDirectory = Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Fixed simulation timestep in seconds.
    /// </summary>
    public float fixedDeltaTime { get; init; }

    /// <summary>
    /// Maximum accepted frame delta in seconds used to clamp unstable frame times.
    /// </summary>
    public float maxFrameDeltaTime { get; init; }

    /// <summary>
    /// Maximum number of fixed-step logic updates executed in a single <see cref="Shell.Tick"/> call.
    /// </summary>
    public int maxUpdateStepsPerTick { get; init; }

    /// <summary>
    /// Whether to use deterministic single-thread jobs instead of work-stealing workers.
    /// </summary>
    public bool useSingleThreadJobSystem { get; init; }

    /// <summary>
    /// Worker thread count for work-stealing jobs. Set to 0 for auto.
    /// </summary>
    public int jobWorkerCount { get; init; }

    /// <summary>
    /// Root folder of the project.
    /// </summary>
    public string projectRootDirectory { get; init; }
}
