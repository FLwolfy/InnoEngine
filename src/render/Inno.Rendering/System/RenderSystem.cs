using System.Diagnostics;
using Inno.Graphics;

namespace Inno.Rendering;

/// <summary>
/// Main high-level rendering entry point.
/// </summary>
public sealed class RenderSystem : IDisposable
{
    private ulong m_frameIndex;
    private RenderFrameStatistics m_lastStatistics = new();
    private readonly RenderResourceCache m_resourceCache = new();
    private readonly GraphicsRenderRuntime? m_graphicsRuntime;

    public RenderSystem(RenderPipeline? pipeline = null, RenderSettings? settings = null)
    {
        this.pipeline = pipeline ?? ForwardPipeline.Create();
        this.settings = settings ?? new RenderSettings();
    }

    public RenderSystem(
        IGraphicsDevice device,
        IGraphicsSwapchain swapchain,
        RenderPipeline? pipeline = null,
        RenderSettings? settings = null,
        string? shaderProfile = null,
        string? shaderAssetRoot = null)
        : this(pipeline, settings)
    {
        m_graphicsRuntime = new GraphicsRenderRuntime(device, swapchain, shaderProfile, shaderAssetRoot);
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

        var context = new RenderPipelineContext
        {
            request = request,
            frame = frame,
            resourceCache = m_resourceCache,
            graphics = m_graphicsRuntime
        };

        try
        {
            m_graphicsRuntime?.BeginFrame(request);
            pipeline.Render(context);
        }
        finally
        {
            m_graphicsRuntime?.EndFrame();
            if (settings.collectStatistics)
            {
                frame.statistics.renderablesSubmitted = request.scene.renderables.items.Count;
                frame.statistics.visibleLights = request.scene.lights.items.Count;
            }
            stopwatch.Stop();
            if (settings.collectStatistics)
            {
                frame.statistics.cpuTime = stopwatch.Elapsed;
                m_lastStatistics = frame.statistics;
            }
        }
    }

    public RenderFrameStatistics GetLastFrameStatistics() => m_lastStatistics;

    public void Dispose()
    {
        m_graphicsRuntime?.Dispose();
    }
}
