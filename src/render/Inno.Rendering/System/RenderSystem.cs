using System.Diagnostics;
using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Main high-level rendering entry point.
/// </summary>
public sealed class RenderSystem
{
    private ulong m_frameIndex;
    private RenderFrameStatistics m_lastStatistics = new();

    public RenderSystem(RenderPipeline? pipeline = null, RenderSettings? settings = null)
    {
        this.pipeline = pipeline ?? ForwardPipeline.Create();
        this.settings = settings ?? new RenderSettings();
    }

    public RenderPipeline pipeline { get; set; }

    public RenderSettings settings { get; }

    public void Render(RenderScene scene, RenderView view, RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(target);

        var request = new RenderRequest
        {
            scene = scene,
            view = view,
            target = target
        };

        Render(request);
    }

    public void Render(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var frame = new RenderFrame
        {
            frameIndex = ++m_frameIndex,
            timestamp = DateTimeOffset.UtcNow
        };

        var cache = new RenderResourceCache();
        var context = new RenderPipelineContext
        {
            request = request,
            frame = frame,
            resourceCache = cache
        };

        pipeline.Render(context);
        frame.statistics.renderablesSubmitted = request.scene.renderables.items.Count;
        frame.statistics.visibleLights = request.scene.lights.items.Count;
        stopwatch.Stop();
        frame.statistics.cpuTime = stopwatch.Elapsed;
        m_lastStatistics = frame.statistics;
    }

    public RenderFrameStatistics GetLastFrameStatistics() => m_lastStatistics;
}
