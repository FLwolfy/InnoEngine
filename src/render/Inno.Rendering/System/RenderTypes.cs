
namespace Inno.Rendering;

/// <summary>
/// Represents global render system settings.
/// </summary>
public sealed class RenderSettings
{
    public bool enableValidation { get; set; }

    public bool collectStatistics { get; set; } = true;
}

/// <summary>
/// Represents a single render invocation request.
/// </summary>
public sealed class RenderRequest
{
    public required RenderScene scene { get; init; }

    public required RenderView view { get; init; }

    public required RenderTarget target { get; init; }
}

/// <summary>
/// Represents per-frame render state.
/// </summary>
public sealed class RenderFrame
{
    public required ulong frameIndex { get; init; }

    public required DateTimeOffset timestamp { get; init; }

    public RenderFrameStatistics statistics { get; } = new();
}

/// <summary>
/// Represents simple per-frame render statistics.
/// </summary>
public sealed class RenderFrameStatistics
{
    public int drawCalls { get; internal set; }

    public int renderablesSubmitted { get; internal set; }

    public int visibleLights { get; internal set; }

    public TimeSpan cpuTime { get; internal set; }
}
