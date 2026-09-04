using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Tests;

public sealed class RenderGraphCompilerTests
{
    private static readonly RenderPhaseId C_GEOMETRY = new("tests.geometry");
    private static readonly RenderPhaseId C_PROCESS = new("tests.process");
    private static readonly RenderPhaseId C_TRANSPARENT = new("tests.transparent");
    private static readonly RenderPhaseId C_POST = new("tests.post");
    private static readonly RenderPhaseId C_FINAL = new("tests.final");

    [Fact]
    public void NameScopesIsolateRepeatedProducerNamesInsideOneFrameGraph()
    {
        RenderGraphBuilder graph = CreateGraph();
        using (graph.BeginNameScope("Request[0] First"))
        {
            graph.AddRasterPass("Visible", C_GEOMETRY, 0, static (_, _) => { })
                .HasSideEffect();
        }
        using (graph.BeginNameScope("Request[1] Second"))
        {
            graph.AddRasterPass("Visible", C_GEOMETRY, 0, static (_, _) => { })
                .HasSideEffect();
        }

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(
            ["Request[0] First/Visible", "Request[1] Second/Visible"],
            PassNames(result.graph!));
    }

    [Fact]
    public void MutationScope_Uncommitted_RollsBackPassesResourcesAndOutputs()
    {
        RenderGraphBuilder graph = CreateGraph();
        using (graph.BeginMutationScope())
        {
            RenderTextureHandle temporary = graph.CreateTexture("Temporary", ColorDescriptor());
            graph.AddRasterPass("Failed Feature", C_GEOMETRY, 0, static (_, _) => { })
                .UseColorAttachment(temporary, 0, RenderLoadAction.Clear);
            graph.MarkOutput(temporary);
        }

        graph.AddRasterPass("Survivor", C_FINAL, 0, static (_, _) => { })
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Survivor"], PassNames(result.graph!));
        Assert.Empty(result.graph!.textures);
    }
    [Fact]
    public void Compile_SchedulesProducerBeforeConsumer()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle intermediate = graph.CreateTexture("Intermediate", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());
        graph.AddRasterPass("Producer", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(intermediate, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("Consumer", C_POST, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear)
            .ReadTexture(intermediate);
        graph.MarkOutput(output);

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Producer", "Consumer"], PassNames(result.graph!));
    }

    [Fact]
    public void Compile_CullsUnconsumedPass()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle unused = graph.CreateTexture("Unused", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());
        graph.AddRasterPass("Unused Pass", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(unused, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("Output Pass", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear);
        graph.MarkOutput(output);

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Output Pass"], PassNames(result.graph!));
        Assert.Equal(1, result.culledPassCount);
    }

    [Fact]
    public void Compile_WithUninitializedRead_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle input = graph.CreateTexture("Input", SampledDescriptor());
        graph.AddRasterPass("Reader", C_GEOMETRY, 0, static (_, _) => { })
            .ReadTexture(input)
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_UNINITIALIZED_READ");
    }

    [Fact]
    public void Compile_WithReadWriteDeclaredAsSeparateAccess_FailsHazardValidation()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle texture = graph.CreateTexture(
            "Storage",
            new RenderTextureDescriptor(
                64,
                64,
                RenderTextureFormat.RGBA16Float,
                RenderTextureUsage.Storage | RenderTextureUsage.Sampled));
        graph.AddComputePass("Hazard", C_PROCESS, 0, static (_, _) => { })
            .ReadTexture(texture);
        ComputePassBuilder writer = graph.AddComputePass(
            "Writer",
            C_PROCESS,
            0,
            static (_, _) => { });
        writer.WriteStorageTexture(texture);
        writer.ReadTexture(texture);
        writer.HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_PASS_HAZARD");
    }

    [Fact]
    public void Compile_ReusesCompatibleNonOverlappingTextures()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle first = graph.CreateTexture("First", ColorDescriptor());
        RenderTextureHandle middle = graph.CreateTexture("Middle", ColorDescriptor());
        RenderTextureHandle second = graph.CreateTexture("Second", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());

        graph.AddRasterPass("A", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(first, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("B", C_PROCESS, 0, static (_, _) => { })
            .UseColorAttachment(middle, 0, RenderLoadAction.Clear)
            .ReadTexture(first);
        graph.AddRasterPass("C", C_TRANSPARENT, 0, static (_, _) => { })
            .UseColorAttachment(second, 0, RenderLoadAction.Clear)
            .ReadTexture(middle);
        graph.AddRasterPass("D", C_POST, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear)
            .ReadTexture(second);
        graph.MarkOutput(output);

        CompiledRenderGraph compiled = graph.Compile().graph!;

        Assert.Equal(compiled.textures[0].physicalSlot, compiled.textures[2].physicalSlot);
    }

    [Fact]
    public void Compile_WhenViewLimitExceeded_FailsClearly()
    {
        RenderGraphBuilder graph = new(1, CreateCapabilities(maxViews: 1));
        graph.AddRasterPass("A", C_GEOMETRY, 0, static (_, _) => { }).HasSideEffect();
        graph.AddRasterPass("B", C_TRANSPARENT, 0, static (_, _) => { }).HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_VIEW_LIMIT");
    }

    [Fact]
    public void Compile_WithPhaseCycle_FailsClearly()
    {
        RenderPhaseId left = new("tests.left");
        RenderPhaseId right = new("tests.right");
        RenderGraphBuilder graph = CreateGraph();
        RenderPassBuilder leftPass = graph.AddRasterPass("Left", left, 0, static (_, _) => { });
        leftPass.Before(right).HasSideEffect();
        RenderPassBuilder rightPass = graph.AddRasterPass("Right", right, 0, static (_, _) => { });
        rightPass.Before(left).HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_CYCLE");
    }

    [Fact]
    public void Compile_WithInvalidAttachmentSubresource_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle texture = graph.CreateTexture(
            "Array",
            new RenderTextureDescriptor(
                128,
                128,
                RenderTextureFormat.RGBA16Float,
                RenderTextureUsage.ColorAttachment,
                mipCount: 2,
                arrayLayers: 2));
        graph.AddRasterPass("Invalid Layer", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(
                texture,
                0,
                RenderLoadAction.Clear,
                mipLevel: 1,
                arrayLayer: 2);
        graph.MarkOutput(texture);

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_ATTACHMENT_SUBRESOURCE");
    }

    [Fact]
    public void Compile_WhenTextureUsageDoesNotMatchPassOperation_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle texture = graph.CreateTexture(
            "Not Storage",
            new RenderTextureDescriptor(
                64,
                64,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled));
        graph.AddComputePass("Storage Writer", C_PROCESS, 0, static (_, _) => { })
            .WriteStorageTexture(texture)
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_RESOURCE_USAGE");
    }

    [Fact]
    public void Compile_WhenStoredOutputIsDiscarded_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle output = graph.CreateTexture("Discarded", ColorDescriptor());
        graph.AddRasterPass("Discard", C_FINAL, 0, static (_, _) => { })
            .UseColorAttachment(
                output,
                0,
                RenderLoadAction.Clear,
                RenderStoreAction.Discard);
        graph.MarkOutput(output);

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_OUTPUT_UNINITIALIZED");
    }

    [Fact]
    public void Compile_WhenBufferCopyCapabilityIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderBufferHandle source = graph.CreateBuffer(
            "Source",
            new RenderBufferDescriptor(16, 4, RenderBufferUsage.CopySource));
        RenderBufferHandle destination = graph.CreateBuffer(
            "Destination",
            new RenderBufferDescriptor(16, 4, RenderBufferUsage.CopyDestination));
        graph.AddCopyPass("Copy", C_PROCESS, 0, static (_, _) => { })
            .CopyBuffer(source, destination)
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_CAPABILITY_UNSUPPORTED");
    }

    [Fact]
    public void Compile_WhenSampledFormatIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = new(
            1,
            CreateCapabilities(
                maxViews: 64,
                sampledFormats: [RenderTextureFormat.RGBA8]));
        _ = graph.CreateTexture(
            "Unsupported Sampled Format",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA16Float,
                RenderTextureUsage.Sampled));

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_FORMAT_SAMPLED_UNSUPPORTED");
    }

    [Fact]
    public void Compile_WhenTextureArrayCapabilityIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = new(
            1,
            CreateCapabilities(
                maxViews: 64,
                features: GraphicsFeature.Compute
                    | GraphicsFeature.StorageBuffer
                    | GraphicsFeature.StorageTexture
                    | GraphicsFeature.TextureBlit));
        _ = graph.CreateTexture(
            "Unsupported Array",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled,
                arrayLayers: 2));

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_TEXTURE_ARRAY_UNSUPPORTED");
    }

    [Fact]
    public void Compile_WhenUnsigned32BitIndexCapabilityIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = new(
            1,
            CreateCapabilities(
                maxViews: 64,
                features: GraphicsFeature.Compute
                    | GraphicsFeature.StorageBuffer
                    | GraphicsFeature.TextureBlit));
        _ = graph.CreateBuffer(
            "Unsupported Indices",
            new RenderBufferDescriptor(3, sizeof(uint), RenderBufferUsage.Index));

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_INDEX32_UNSUPPORTED");
    }

    [Fact]
    public void Compile_WhenVolumeTextureCapabilityIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = new(1, CreateCapabilities(maxViews: 64));
        _ = graph.CreateTexture(
            "Unsupported Volume",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled,
                dimension: RenderTextureDimension.Texture3D,
                depth: 8));

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_TEXTURE_3D_UNSUPPORTED");
    }

    [Fact]
    public void Compile_WithSupportedVolumeAndCubemapTextures_Succeeds()
    {
        RenderGraphBuilder graph = new(
            1,
            CreateCapabilities(
                maxViews: 64,
                features: GraphicsFeature.Texture3D | GraphicsFeature.TextureCubeArray,
                sampled3DFormats: [RenderTextureFormat.RGBA8],
                sampledCubeFormats: [RenderTextureFormat.RGBA8]));
        _ = graph.CreateTexture(
            "Volume",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled,
                dimension: RenderTextureDimension.Texture3D,
                depth: 8));
        _ = graph.CreateTexture(
            "Cubemap Array",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled,
                arrayLayers: 2,
                dimension: RenderTextureDimension.Cube));

        Assert.True(graph.Compile().succeeded);
    }

    [Fact]
    public void Compile_WhenMultisampleFormatCapabilityIsMissing_FailsClearly()
    {
        RenderGraphBuilder graph = new(1, CreateCapabilities(maxViews: 64));
        RenderTextureHandle target = graph.CreateTexture(
            "Unsupported MSAA",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment,
                sampleCount: 4));
        graph.AddRasterPass("MSAA", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(target, 0, RenderLoadAction.Clear);
        graph.MarkOutput(target);

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(
            result.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_FORMAT_MSAA_UNSUPPORTED");
    }

    [Fact]
    public void Compile_StorageTextureAccessHonorsReadAndWriteFormatCapabilities()
    {
        RenderGraphBuilder readGraph = new(
            1,
            CreateCapabilities(
                maxViews: 64,
                storageReadFormats: [RenderTextureFormat.RGBA8],
                storageWriteFormats: []));
        RenderTextureHandle readTexture = readGraph.CreateTexture(
            "Read Only",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Storage));
        readGraph.AddRasterPass("Initialize", C_GEOMETRY, 0, static (_, _) => { })
            .UseColorAttachment(readTexture, 0, RenderLoadAction.Clear);
        readGraph.AddComputePass("Read", C_PROCESS, 0, static (_, _) => { })
            .ReadStorageTexture(readTexture)
            .HasSideEffect();

        RenderGraphCompileResult readResult = readGraph.Compile();

        Assert.True(readResult.succeeded);

        RenderGraphBuilder writeGraph = new(
            2,
            CreateCapabilities(
                maxViews: 64,
                storageReadFormats: [],
                storageWriteFormats: [RenderTextureFormat.RGBA8]));
        RenderTextureHandle writeTexture = writeGraph.CreateTexture(
            "Write Only",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Storage));
        writeGraph.AddComputePass("Write", C_PROCESS, 0, static (_, _) => { })
            .WriteStorageTexture(writeTexture)
            .HasSideEffect();

        RenderGraphCompileResult writeResult = writeGraph.Compile();

        Assert.True(writeResult.succeeded);

        RenderGraphBuilder readWriteGraph = new(
            3,
            CreateCapabilities(
                maxViews: 64,
                storageReadFormats: [],
                storageWriteFormats: [RenderTextureFormat.RGBA8]));
        RenderTextureHandle readWriteTexture = readWriteGraph.CreateTexture(
            "Read Write",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Storage));
        readWriteGraph.AddComputePass("Read Write", C_PROCESS, 0, static (_, _) => { })
            .ReadWriteStorageTexture(readWriteTexture)
            .HasSideEffect();

        RenderGraphCompileResult readWriteResult = readWriteGraph.Compile();

        Assert.False(readWriteResult.succeeded);
        Assert.Contains(
            readWriteResult.diagnostics,
            diagnostic => diagnostic.code == "RENDER_GRAPH_STORAGE_TEXTURE_ACCESS_UNSUPPORTED");
    }

    [Fact]
    public void Execute_WhenPassFails_StillEndsPassAndGraph()
    {
        RenderGraphBuilder graph = CreateGraph();
        graph.AddRasterPass(
                "Failure",
                C_GEOMETRY,
                0,
                static (_, _) => throw new InvalidOperationException("failure"))
            .HasSideEffect();
        CompiledRenderGraph compiled = graph.Compile().graph!;
        RecordingBackend backend = new();

        Assert.Throws<InvalidOperationException>(() => compiled.Execute(backend, 12));

        Assert.Equal(["BeginGraph", "Begin:Failure", "End:Failure", "EndGraph"], backend.calls);
    }

    [Fact]
    public void Execute_ParallelRecordingOverlapsEligibleCallbacksAndReplaysInGraphOrder()
    {
        RenderGraphBuilder graph = CreateGraph();
        int activeRecorders = 0;
        int maximumRecorders = 0;
        void Record(int drawCount, RenderPassContext context)
        {
            int active = Interlocked.Increment(ref activeRecorders);
            int observed;
            while (active > (observed = Volatile.Read(ref maximumRecorders)))
                _ = Interlocked.CompareExchange(ref maximumRecorders, active, observed);
            Thread.Sleep(40);
            context.commands.Draw(drawCount);
            _ = Interlocked.Decrement(ref activeRecorders);
        }

        graph.AddRasterPass("Parallel A", C_GEOMETRY, 1, Record)
            .AllowParallelRecording()
            .HasSideEffect();
        graph.AddRasterPass("Parallel B", C_PROCESS, 2, Record)
            .AllowParallelRecording()
            .HasSideEffect();
        graph.AddRasterPass("Serial", C_FINAL, 3, Record)
            .HasSideEffect();
        CompiledRenderGraph compiled = graph.Compile().graph!;
        RecordingBackend backend = new();

        compiled.Execute(backend, 42);

        Assert.Equal(
            [
                "BeginGraph",
                "Begin:Parallel A", "Draw:Parallel A:1", "End:Parallel A",
                "Begin:Parallel B", "Draw:Parallel B:2", "End:Parallel B",
                "Begin:Serial", "Draw:Serial:3", "End:Serial",
                "EndGraph"
            ],
            backend.calls);
        Assert.Equal(
            [RenderPassRecordingMode.Parallel, RenderPassRecordingMode.Parallel, RenderPassRecordingMode.Serial],
            compiled.passes.Select(static pass => pass.recordingMode));
        if (Environment.ProcessorCount > 1)
            Assert.True(maximumRecorders > 1, "Parallel pass callbacks did not overlap.");
    }

    private static RenderGraphBuilder CreateGraph() => new(1, CreateCapabilities(64));

    private static GraphicsCapabilities CreateCapabilities(
        int maxViews,
        GraphicsFeature? features = null,
        IReadOnlyList<RenderTextureFormat>? sampledFormats = null,
        IReadOnlyList<RenderTextureFormat>? sampled3DFormats = null,
        IReadOnlyList<RenderTextureFormat>? sampledCubeFormats = null,
        IReadOnlyList<RenderTextureFormat>? storageReadFormats = null,
        IReadOnlyList<RenderTextureFormat>? storageWriteFormats = null)
        => new(
            GraphicsBackend.Noop,
            features
                ?? (GraphicsFeature.Compute
                    | GraphicsFeature.StorageBuffer
                    | GraphicsFeature.StorageTexture
                    | GraphicsFeature.TextureBlit
                    | GraphicsFeature.Texture2DArray
                    | GraphicsFeature.Index32),
            new GraphicsLimits(maxViews, 8, 16384, 16),
            sampledFormats ?? Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            storageReadFormats ??
                [RenderTextureFormat.RGBA8, RenderTextureFormat.RGBA16Float, RenderTextureFormat.R32Float],
            storageWriteFormats ??
                [RenderTextureFormat.RGBA8, RenderTextureFormat.RGBA16Float, RenderTextureFormat.R32Float],
            false,
            false,
            sampled3DFormats,
            sampledCubeFormats);

    private static RenderTextureDescriptor ColorDescriptor()
        => new(
            128,
            128,
            RenderTextureFormat.RGBA16Float,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private static RenderTextureDescriptor SampledDescriptor()
        => new(128, 128, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled);

    private static List<string> PassNames(CompiledRenderGraph graph)
    {
        List<string> names = [];
        foreach (CompiledRenderPass pass in graph.passes)
        {
            names.Add(pass.name);
        }

        return names;
    }

    private sealed class RecordingBackend : IRenderGraphBackend
    {
        public List<string> calls { get; } = [];

        public void BeginGraph(CompiledRenderGraph graph) => calls.Add("BeginGraph");

        public RenderCommandEncoder BeginPass(CompiledRenderPass pass)
        {
            calls.Add($"Begin:{pass.name}");
            return new EmptyEncoder(drawCount => calls.Add($"Draw:{pass.name}:{drawCount}"));
        }

        public void EndPass(CompiledRenderPass pass) => calls.Add($"End:{pass.name}");

        public void EndGraph(CompiledRenderGraph graph) => calls.Add("EndGraph");
    }

    private sealed class EmptyEncoder : RenderCommandEncoder
    {
        private readonly Action<int>? m_onDraw;

        internal EmptyEncoder(Action<int>? onDraw = null)
        {
            m_onDraw = onDraw;
        }

        public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline) { }
        public override void BindComputePipeline(ComputePipelineHandle pipeline) { }
        public override void BindTexture(
            RenderBindingId binding,
            RenderTextureHandle texture,
            RenderSamplerState sampler) { }
        public override void BindTexture(
            RenderBindingId binding,
            PersistentTextureHandle texture,
            RenderSamplerState sampler) { }
        public override void BindStorageTexture(
            RenderBindingId binding,
            RenderTextureHandle texture,
            int mipLevel = 0) { }
        public override void BindStorageTexture(
            RenderBindingId binding,
            PersistentTextureHandle texture,
            int mipLevel = 0) { }
        public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer) { }
        public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer) { }
        public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value) { }
        public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix) { }
        public override void SetRasterState(RenderRasterState state) { }
        public override void SetStencil(RenderStencilState state) { }
        public override void SetViewport(int x, int y, int width, int height) { }
        public override void SetScissor(int x, int y, int width, int height) { }
        public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0) { }
        public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0) { }
        public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0) { }
        public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0) { }
        public override void BindInstanceBuffer(
            RenderBufferHandle buffer,
            int firstInstance,
            int instanceCount) { }
        public override void BindInstanceBuffer(
            PersistentBufferHandle buffer,
            int firstInstance,
            int instanceCount) { }
        public override void Draw(int vertexCount, int instanceCount = 1) => m_onDraw?.Invoke(vertexCount);
        public override void DrawProcedural(int vertexCount, int instanceCount = 1) { }
        public override void DrawIndexed(int indexCount, int instanceCount = 1) { }
        public override void DrawIndirect(
            RenderBufferHandle buffer,
            int firstCommand = 0,
            int commandCount = 1) { }
        public override void DrawIndirect(
            PersistentBufferHandle buffer,
            int firstCommand = 0,
            int commandCount = 1) { }
        public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1) { }
        public override void DispatchIndirect(
            RenderBufferHandle buffer,
            int firstCommand = 0,
            int commandCount = 1) { }
        public override void DispatchIndirect(
            PersistentBufferHandle buffer,
            int firstCommand = 0,
            int commandCount = 1) { }
        public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination) { }
        public override void BlitTexture(
            RenderTextureHandle source,
            RenderTextureRegion sourceRegion,
            RenderTextureHandle destination,
            RenderTextureRegion destinationRegion) { }
        public override void CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination) { }
    }
}
