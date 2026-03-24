
namespace Inno.Rendering;

/// <summary>
/// Represents simple per-frame render statistics.
/// </summary>
public sealed class RenderFrameStatistics
{
    public int drawCalls { get; internal set; }

    public int renderablesSubmitted { get; internal set; }

    public int visibleLights { get; internal set; }

    public int renderGraphPassCount { get; internal set; }

    public int renderGraphResourceCount { get; internal set; }

    public TimeSpan cpuTime { get; internal set; }
}
