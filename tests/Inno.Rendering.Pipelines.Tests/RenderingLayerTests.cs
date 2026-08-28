using System;
using System.Collections.Generic;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Layers;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

[Collection(RenderingPipelineTestCollection.NAME)]
public sealed class RenderingLayerTests
{
    [Fact]
    public void Frame_ExecutesAllRequestsAndAdvancesDeviceExactlyOnce()
    {
        var device = new RecordingDevice();
        var diagnostics = new RecordingDiagnostics();
        var layer = new RenderingLayer(
            device,
            new RenderPipelineAsset(),
            new SideEffectPipeline(),
            new EmptyExecutor(),
            diagnostics);
        layer.OnAttach();
        layer.Submit(Request("B", 2));
        layer.Submit(Request("A", 1));

        layer.OnBeforeRender(1f / 60f);
        layer.OnAfterRender(1f / 60f);

        Assert.Equal(1, device.beginCount);
        Assert.Equal(1, device.endCount);
        Assert.Equal(2, device.executeCount);
        Assert.Equal(2, GraphicsSettings.frameStatistics?.viewCount);
        Assert.Empty(diagnostics.values);
        layer.OnDetach();
    }

    [Fact]
    public void FeatureFailure_RollsBackPartialGraphAndKeepsFrameRunning()
    {
        var device = new RecordingDevice();
        var diagnostics = new RecordingDiagnostics();
        var asset = new RenderPipelineAsset();
        asset.SetFeatures([new RenderFeatureConfiguration("tests.failure")]);
        var layer = new RenderingLayer(
            device,
            asset,
            new SideEffectPipeline(),
            new EmptyExecutor(),
            diagnostics,
            new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal)
            {
                ["tests.failure"] = new FailingFeature()
            });
        layer.Submit(Request("View", 0));

        layer.OnBeforeRender(1f / 60f);
        layer.OnAfterRender(1f / 60f);

        CompiledRenderGraph graph = Assert.Single(device.graphs);
        Assert.Equal("View/Main", Assert.Single(graph.passes).name);
        Assert.Contains(diagnostics.values, static value => value.code == "RENDER_FEATURE_FAILED");
        Assert.Equal(1, device.endCount);
    }

    [Fact]
    public void FrameContributor_ExecutesInOneFinalGraphBeforeSingleFrameSubmission()
    {
        var device = new RecordingDevice();
        var contributor = new TestContributor();
        var layer = new RenderingLayer(
            device,
            new RenderPipelineAsset(),
            new SideEffectPipeline(),
            new EmptyExecutor(),
            new RecordingDiagnostics(),
            contributors: [contributor]);
        layer.Submit(Request("View", 0));

        layer.OnBeforeRender(1f / 60f);
        layer.OnAfterRender(1f / 60f);

        Assert.Equal(1, contributor.prepareCount);
        Assert.Equal(2, device.executeCount);
        Assert.Equal("UI", Assert.Single(device.graphs[1].passes).name);
        Assert.Equal(1, device.endCount);
    }

    private static RenderRequest Request(string name, int priority)
        => new(
            name,
            new RenderView(
                Matrix.identity,
                Matrix.identity,
                Vector3.ZERO,
                32,
                32,
                GameLayerMask.everything),
            RenderTarget.backbuffer,
            priority: priority);

    private sealed class SideEffectPipeline : RenderPipeline
    {
        public override void Build(RenderPipelineContext context)
            => context.graph.AddRasterPass(
                    $"{context.request.name}/Main",
                    BuiltinRenderPhases.afterRendering,
                    0,
                    static (_, _) => { })
                .HasSideEffect();
    }

    private sealed class FailingFeature : RenderPipelineFeature
    {
        public override void AddRenderPasses(RenderFeatureContext context)
        {
            context.graph.AddRasterPass(
                    "Failed Feature Pass",
                    BuiltinRenderPhases.postProcessing,
                    0,
                    static (_, _) => { })
                .HasSideEffect();
            throw new InvalidOperationException("candidate failed");
        }
    }

    internal sealed class EmptyExecutor : IRenderPipelineExecutor
    {
        public void PrepareFrame(ulong frameIndex) { }

        public RenderTextureHandle ImportTarget(RenderGraphBuilder graph, RenderTexture target)
            => throw new NotSupportedException();

        public bool TryGetTargetTexture(RenderTexture target, out PersistentTextureHandle texture)
        {
            texture = default;
            return false;
        }

        public void ReleaseTarget(RenderTexture target) { }

        public void Prepare(RenderPipelineOperation operation) { }
        public void Execute(RenderPipelineOperation operation, RenderPassContext context) { }
    }

    private sealed class TestContributor : IRenderFrameGraphContributor
    {
        public int prepareCount { get; private set; }

        public void PrepareFrame(ulong frameIndex)
        {
            _ = frameIndex;
            prepareCount++;
        }

        public void AddRenderPasses(RenderGraphBuilder graph, ulong frameIndex)
        {
            _ = frameIndex;
            graph.AddRasterPass("UI", BuiltinRenderPhases.userInterface, 0, static (_, _) => { })
                .HasSideEffect();
        }
    }

    internal sealed class RecordingDiagnostics : IRenderDiagnosticSink
    {
        public List<RenderDiagnostic> values { get; } = [];
        public void Publish(RenderDiagnostic diagnostic) => values.Add(diagnostic);
    }

    internal sealed class RecordingDevice : IRenderDevice
    {
        public GraphicsCapabilities capabilities { get; } = new(
            GraphicsBackend.Noop,
            GraphicsFeature.None,
            new GraphicsLimits(64, 4, 1024, 4),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            false,
            false);
        public uint generation => 1;
        public int beginCount { get; private set; }
        public int endCount { get; private set; }
        public int executeCount { get; private set; }
        public List<CompiledRenderGraph> graphs { get; } = [];
        public void BeginFrame() => beginCount++;
        public void Execute(CompiledRenderGraph graph, ulong frameIndex)
        {
            _ = frameIndex;
            executeCount++;
            graphs.Add(graph);
        }
        public uint EndFrame()
        {
            endCount++;
            return 0;
        }
        public void ResizeBackbuffer(int width, int height) { }
        public PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name) => default;
        public void UpdateTexture(
            PersistentTextureHandle texture,
            ReadOnlySpan<byte> data,
            int mipLevel = 0,
            int arrayLayer = 0) { }
        public void DestroyTexture(PersistentTextureHandle texture) { }
        public PersistentBufferHandle CreateBuffer(
            PersistentBufferDescriptor descriptor,
            ReadOnlySpan<byte> initialData,
            string name) => default;
        public void DestroyBuffer(PersistentBufferHandle buffer) { }
        public void UpdateBuffer(PersistentBufferHandle buffer, ReadOnlySpan<byte> data, int startElement = 0) { }
        public GraphicsPipelineHandle CreateGraphicsPipeline(
            GraphicsPipelineDescriptor descriptor,
            string name) => default;
        public void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) { }
        public ComputePipelineHandle CreateComputePipeline(
            ComputePipelineDescriptor descriptor,
            string name) => default;
        public void DestroyComputePipeline(ComputePipelineHandle pipeline) { }
        public void Dispose() { }
    }
}
